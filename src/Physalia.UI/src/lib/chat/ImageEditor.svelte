<script lang="ts">
	// The image editor — the Image Mark Up human tool's whole surface. An image comes in, the human
	// draws on it (freehand, text notes, arrows, an eraser that takes back marks without touching the
	// picture), and a flattened PNG comes out.
	//
	// Marks are kept as OBJECTS in the image's own pixel space, never painted into the bitmap: that is
	// what lets the eraser lift a mark off the picture underneath, what makes undo/redo exact, and what
	// keeps the committed image at full capture resolution however small the window is. The canvas is
	// therefore a view — sized to the natural image and scaled down by CSS — redrawn from the mark list
	// on every change. Stroke widths and font sizes are stored in natural pixels but CHOSEN from the
	// on-screen scale (a 12pt note means 12pt as the human sees it), so mark-up looks the same on a
	// 900px viewport capture and a 3000px one.
	import { onMount } from 'svelte';
	import PencilIcon from '@lucide/svelte/icons/pencil';
	import TypeIcon from '@lucide/svelte/icons/type';
	import ArrowRightIcon from '@lucide/svelte/icons/arrow-right';
	import EraserIcon from '@lucide/svelte/icons/eraser';
	import RotateCcwIcon from '@lucide/svelte/icons/rotate-ccw';
	import RotateCwIcon from '@lucide/svelte/icons/rotate-cw';
	import XIcon from '@lucide/svelte/icons/x';
	import CheckIcon from '@lucide/svelte/icons/check';
	import { stripDataUrl } from '$lib/bridge';

	interface Props {
		/** The image to mark up: raw base64, no data: prefix. */
		base64: string;
		mediaType: string;
		/** What the picture is, for the header — "Geometry snapshot", "Attached image". */
		label?: string;
		/** Confirm: the flattened image as raw base64 PNG (mark-up burnt in). */
		onconfirm: (base64: string) => void;
		/** Cancel: the mark-up is discarded. What happens to the image itself is the caller's call. */
		oncancel: () => void;
	}

	let { base64, mediaType, label = 'Image', onconfirm, oncancel }: Props = $props();

	type Point = { x: number; y: number };

	/** One mark-up object, in image pixel space. */
	type Mark =
		| { kind: 'stroke'; colour: string; width: number; points: Point[] }
		| { kind: 'arrow'; colour: string; width: number; from: Point; to: Point }
		| { kind: 'text'; colour: string; size: number; text: string; at: Point };

	type Tool = 'pen' | 'text' | 'arrow' | 'eraser';

	// The mockup's palette, read left-to-right, top-to-bottom. Red is the default: it is the colour a
	// person reaches for to say "look here".
	const PALETTE = [
		['#e8d84b', '#c0342b', '#5b84c4'],
		['#7fb03f', '#6c63b5', '#d9752b'],
		['#ffffff', '#9a9a9a', '#1a1a1a']
	];
	const DEFAULT_COLOUR = '#c0342b';

	// On-screen sizes. Each is divided by the display scale before being stored, so what is committed
	// at full resolution matches what was drawn on a scaled-down view.
	const PEN_WIDTH_CSS = 3;
	const TEXT_SIZE_CSS = 16; // 12pt at 96dpi
	const ERASER_RADIUS_CSS = 10;
	const FONT_STACK =
		'ui-sans-serif, system-ui, -apple-system, "Segoe UI", Roboto, Helvetica, Arial, sans-serif';

	let tool = $state<Tool>('pen');
	let colour = $state(DEFAULT_COLOUR);

	let marks = $state<Mark[]>([]);
	// Whole-list snapshots rather than inverse operations: a mark list is a handful of small objects,
	// and snapshots make undo of an ERASE (which removes several marks at once) the same code as undo
	// of a single stroke.
	let past = $state<Mark[][]>([]);
	let future = $state<Mark[][]>([]);

	// The mark being drawn right now — rendered, but not yet in `marks` and not yet undoable.
	let drafting = $state<Mark | null>(null);
	// The arrow tool's first click. The arrow previews from here to the cursor until the second click.
	let arrowStart = $state<Point | null>(null);
	let cursor = $state<Point | null>(null);
	// An open text note: where it goes and what has been typed so far. It is drawn ON the canvas by the
	// same code that draws a committed note, and typed through this component's own key handler — there
	// is deliberately no <input> element. An overlaid input has to be focused to receive a keystroke, has
	// to be positioned by offsetParent arithmetic, and is clipped by the frame's overflow-hidden; any one
	// of those failing leaves a text tool that silently does nothing, which is what it did in Rhino's
	// WebView. Drawing on the canvas cannot fail differently from the pen.
	let typing = $state<{ at: Point; value: string } | null>(null);
	// Whether the eraser gesture in progress has already taken its history snapshot. One gesture is one
	// undo step, and a gesture that rubs out nothing takes none at all — an undo that visibly does
	// nothing is worse than no undo.
	let erasedThisGesture = false;

	let image = $state<HTMLImageElement | null>(null);
	let naturalWidth = $state(0);
	let naturalHeight = $state(0);
	let canvas = $state<HTMLCanvasElement | null>(null);
	let frameWidth = $state(0);
	let frameHeight = $state(0);

	// How far the picture is scaled down to fit its frame — the canvas's DISPLAY size and nothing else.
	// Mark sizes come from imagePerCss() instead, off the live rect. 1 until both are measured, and never
	// allowed to reach 0.
	let scale = $derived.by(() => {
		if (!naturalWidth || !naturalHeight || !frameWidth || !frameHeight) {
			return 1;
		}
		return Math.min(frameWidth / naturalWidth, frameHeight / naturalHeight, 1) || 1;
	});
	let displayWidth = $derived(naturalWidth * scale);
	let displayHeight = $derived(naturalHeight * scale);

	let canUndo = $derived(past.length > 0);
	let canRedo = $derived(future.length > 0);

	onMount(() => {
		const element = new Image();
		element.onload = () => {
			naturalWidth = element.naturalWidth;
			naturalHeight = element.naturalHeight;
			image = element;
		};
		element.src = `data:${mediaType || 'image/png'};base64,${base64}`;
	});

	// Whole-window shortcuts: the modal owns the keyboard while it is up. Escape backs out of the
	// pending gesture first (a half-drawn arrow, an open note) and only then out of the editor, so it
	// never throws away mark-up the human is still placing.
	$effect(() => {
		const onKey = (e: KeyboardEvent) => {
			if (e.key === 'Escape') {
				if (typing) {
					typing = null;
				} else if (arrowStart) {
					arrowStart = null;
				} else {
					oncancel();
				}
				e.preventDefault();
				return;
			}
			// An open note takes the keyboard: Enter places it, Backspace rubs out a character, and any
			// single printable key (space included) appends. Every one is consumed, so nothing leaks
			// through to the chat window behind. No IME composition and no caret movement — a mark-up
			// note is a few words, not a document.
			if (typing) {
				if (e.key === 'Enter') {
					commitTyping();
					e.preventDefault();
				} else if (e.key === 'Backspace') {
					typing = { ...typing, value: typing.value.slice(0, -1) };
					e.preventDefault();
				} else if (e.key.length === 1 && !e.ctrlKey && !e.metaKey && !e.altKey) {
					typing = { ...typing, value: typing.value + e.key };
					e.preventDefault();
				}
				return;
			}
			const ctrl = e.ctrlKey || e.metaKey;
			if (ctrl && e.key.toLowerCase() === 'z') {
				if (e.shiftKey) {
					redo();
				} else {
					undo();
				}
				e.preventDefault();
			} else if (ctrl && e.key.toLowerCase() === 'y') {
				redo();
				e.preventDefault();
			}
		};
		window.addEventListener('keydown', onKey);
		return () => window.removeEventListener('keydown', onKey);
	});

	// ---- history -------------------------------------------------------------------------------

	function commit(next: Mark[]) {
		past = [...past, marks];
		future = [];
		marks = next;
	}

	function undo() {
		if (past.length === 0) {
			return;
		}
		future = [marks, ...future];
		marks = past[past.length - 1];
		past = past.slice(0, -1);
	}

	function redo() {
		if (future.length === 0) {
			return;
		}
		past = [...past, marks];
		marks = future[0];
		future = future.slice(1);
	}

	// ---- geometry ------------------------------------------------------------------------------

	// Pointer position in IMAGE pixels. Read off the canvas's own rect rather than its frame's, so it
	// stays right whatever the letterboxing around it.
	function at(e: PointerEvent): Point {
		if (!canvas) {
			return { x: 0, y: 0 };
		}
		const rect = canvas.getBoundingClientRect();
		const sx = rect.width ? naturalWidth / rect.width : 1;
		const sy = rect.height ? naturalHeight / rect.height : 1;
		return { x: (e.clientX - rect.left) * sx, y: (e.clientY - rect.top) * sy };
	}

	// Image pixels per CSS pixel, read from the canvas's LIVE rect — the same measurement `at()` maps
	// pointers with. Every stored size goes through this rather than through `scale`, which is derived
	// from a clientWidth binding and can lag a frame behind the rect during a resize: a mark whose width
	// disagrees with the coordinates it was drawn at is a mark in the wrong place at the wrong size.
	function imagePerCss(): number {
		const rect = canvas?.getBoundingClientRect();
		if (!rect || !rect.width || !naturalWidth) {
			return 1;
		}
		return naturalWidth / rect.width;
	}

	function distanceToSegment(p: Point, a: Point, b: Point): number {
		const dx = b.x - a.x;
		const dy = b.y - a.y;
		const lengthSquared = dx * dx + dy * dy;
		if (lengthSquared === 0) {
			return Math.hypot(p.x - a.x, p.y - a.y);
		}
		let t = ((p.x - a.x) * dx + (p.y - a.y) * dy) / lengthSquared;
		t = Math.max(0, Math.min(1, t));
		return Math.hypot(p.x - (a.x + t * dx), p.y - (a.y + t * dy));
	}

	// Does the eraser at `p` (radius in image pixels) touch this mark? Strokes and arrows are tested
	// per SEGMENT, not per sampled point — a fast drag leaves long gaps between the points it recorded,
	// and a mark you can see but cannot rub out is worse than no eraser at all.
	function hits(mark: Mark, p: Point, radius: number): boolean {
		if (mark.kind === 'stroke') {
			if (mark.points.length === 1) {
				return Math.hypot(p.x - mark.points[0].x, p.y - mark.points[0].y) <= radius + mark.width;
			}
			for (let i = 1; i < mark.points.length; i++) {
				if (distanceToSegment(p, mark.points[i - 1], mark.points[i]) <= radius + mark.width) {
					return true;
				}
			}
			return false;
		}
		if (mark.kind === 'arrow') {
			return distanceToSegment(p, mark.from, mark.to) <= radius + mark.width;
		}
		// Text: its drawn box. The baseline is at `at.y`, so the glyphs sit above it.
		const width = measureText(mark);
		return (
			p.x >= mark.at.x - radius &&
			p.x <= mark.at.x + width + radius &&
			p.y >= mark.at.y - mark.size - radius &&
			p.y <= mark.at.y + mark.size * 0.3 + radius
		);
	}

	function measureText(mark: Mark & { kind: 'text' }): number {
		const context = canvas?.getContext('2d');
		if (!context) {
			return mark.text.length * mark.size * 0.55;
		}
		context.save();
		context.font = `${mark.size}px ${FONT_STACK}`;
		const width = context.measureText(mark.text).width;
		context.restore();
		return width;
	}

	// ---- drawing -------------------------------------------------------------------------------

	function paintMark(context: CanvasRenderingContext2D, mark: Mark) {
		context.strokeStyle = mark.colour;
		context.fillStyle = mark.colour;
		context.lineCap = 'round';
		context.lineJoin = 'round';

		if (mark.kind === 'stroke') {
			context.lineWidth = mark.width;
			context.beginPath();
			// A single-point stroke (a tap) is a dot, not a zero-length line — stroke() draws nothing
			// at all for one point.
			if (mark.points.length === 1) {
				context.arc(mark.points[0].x, mark.points[0].y, mark.width / 2, 0, Math.PI * 2);
				context.fill();
				return;
			}
			context.moveTo(mark.points[0].x, mark.points[0].y);
			for (let i = 1; i < mark.points.length; i++) {
				context.lineTo(mark.points[i].x, mark.points[i].y);
			}
			context.stroke();
			return;
		}

		if (mark.kind === 'arrow') {
			const head = Math.max(mark.width * 4, 8);
			const angle = Math.atan2(mark.to.y - mark.from.y, mark.to.x - mark.from.x);
			const spread = 0.42; // ~24°: a head that reads at a glance without looking like a dart
			context.lineWidth = mark.width;
			context.beginPath();
			context.moveTo(mark.from.x, mark.from.y);
			context.lineTo(mark.to.x, mark.to.y);
			context.stroke();
			context.beginPath();
			context.moveTo(mark.to.x, mark.to.y);
			context.lineTo(
				mark.to.x - head * Math.cos(angle - spread),
				mark.to.y - head * Math.sin(angle - spread)
			);
			context.moveTo(mark.to.x, mark.to.y);
			context.lineTo(
				mark.to.x - head * Math.cos(angle + spread),
				mark.to.y - head * Math.sin(angle + spread)
			);
			context.stroke();
			return;
		}

		context.font = `${mark.size}px ${FONT_STACK}`;
		context.textBaseline = 'alphabetic';
		context.fillText(mark.text, mark.at.x, mark.at.y);
	}

	// Paint the picture, then every mark on top, into any context at natural size — used both for the
	// on-screen canvas and for the off-screen one the confirm flattens.
	function paint(context: CanvasRenderingContext2D, list: Mark[]) {
		context.clearRect(0, 0, naturalWidth, naturalHeight);
		if (image) {
			context.drawImage(image, 0, 0, naturalWidth, naturalHeight);
		}
		for (const mark of list) {
			paintMark(context, mark);
		}
	}

	// The live view: committed marks, plus whatever is mid-gesture (the stroke under the pointer, the
	// previewing arrow).
	$effect(() => {
		const context = canvas?.getContext('2d');
		if (!context || !image) {
			return;
		}
		const list = [...marks];
		if (drafting) {
			list.push(drafting);
		}
		if (arrowStart && cursor) {
			list.push({
				kind: 'arrow',
				colour,
				width: PEN_WIDTH_CSS * imagePerCss(),
				from: arrowStart,
				to: cursor
			});
		}
		// The note being typed, drawn exactly as it will be committed.
		const pending: (Mark & { kind: 'text' }) | null = typing
			? {
					kind: 'text',
					colour,
					size: TEXT_SIZE_CSS * imagePerCss(),
					text: typing.value,
					at: typing.at
				}
			: null;
		if (pending) {
			list.push(pending);
		}
		paint(context, list);
		// A caret, so an empty note still shows the human that the click landed and the keyboard is
		// theirs. Drawn here rather than in paint(), which is also what the confirm flattens.
		if (pending) {
			context.fillStyle = pending.colour;
			const x = pending.at.x + measureText(pending);
			const bar = Math.max(1, pending.size * 0.08);
			context.fillRect(x + bar, pending.at.y - pending.size * 0.8, bar, pending.size);
		}
	});

	// ---- pointer -------------------------------------------------------------------------------

	function onPointerDown(e: PointerEvent) {
		if (!image || e.button !== 0) {
			return;
		}
		// Cancel the press's default action — the compatibility mouse events, and with them the native
		// drag/select the OS would start on an image-bearing element. A drawing surface wants none of it.
		e.preventDefault();
		// An open note commits on the next click anywhere — the same gesture that would start the next
		// mark, so a note is never left hanging by carrying on drawing.
		if (typing) {
			commitTyping();
			return;
		}
		const point = at(e);
		capturePointer(e.pointerId);

		if (tool === 'pen') {
			drafting = { kind: 'stroke', colour, width: PEN_WIDTH_CSS * imagePerCss(), points: [point] };
			return;
		}
		if (tool === 'arrow') {
			if (!arrowStart) {
				arrowStart = point;
				cursor = point;
			} else {
				const arrow: Mark = {
					kind: 'arrow',
					colour,
					width: PEN_WIDTH_CSS * imagePerCss(),
					from: arrowStart,
					to: point
				};
				arrowStart = null;
				commit([...marks, arrow]);
			}
			return;
		}
		if (tool === 'text') {
			// Nothing to focus and nothing to lay out: the note appears on the canvas (with a caret) and
			// the keyboard handler above feeds it.
			typing = { at: point, value: '' };
			return;
		}
		erasedThisGesture = false;
		erase(point);
	}

	function onPointerMove(e: PointerEvent) {
		if (!image) {
			return;
		}
		const point = at(e);
		if (arrowStart) {
			cursor = point;
		}
		if (drafting?.kind === 'stroke' && e.buttons === 1) {
			drafting = { ...drafting, points: [...drafting.points, point] };
			return;
		}
		if (tool === 'eraser' && e.buttons === 1) {
			erase(point);
		}
	}

	// Pointer capture keeps the moves coming when a drag wanders off the canvas — an optimisation, not
	// a requirement, so a refused capture must never abort the stroke it was taken for.
	function capturePointer(id: number) {
		try {
			canvas?.setPointerCapture(id);
		} catch {
			// no capture; the gesture still works inside the canvas
		}
	}

	function onPointerUp(e: PointerEvent) {
		if (canvas?.hasPointerCapture(e.pointerId)) {
			canvas.releasePointerCapture(e.pointerId);
		}
		if (drafting) {
			commit([...marks, drafting]);
			drafting = null;
		}
	}

	function erase(point: Point) {
		const radius = ERASER_RADIUS_CSS * imagePerCss();
		const kept = marks.filter((mark) => !hits(mark, point, radius));
		if (kept.length === marks.length) {
			return;
		}
		if (!erasedThisGesture) {
			past = [...past, marks];
			future = [];
			erasedThisGesture = true;
		}
		marks = kept;
	}

	function commitTyping() {
		if (!typing) {
			return;
		}
		const value = typing.value.trim();
		const where = typing.at;
		typing = null;
		if (value) {
			commit([
				...marks,
				{ kind: 'text', colour, size: TEXT_SIZE_CSS * imagePerCss(), text: value, at: where }
			]);
		}
	}

	// ---- commit --------------------------------------------------------------------------------

	// Flatten at NATURAL size, never at the size on screen: the model should get the capture's own
	// resolution, whatever the window happened to be doing while it was drawn on.
	function confirm() {
		// Anything mid-gesture counts as drawn — the human pressed confirm, not cancel.
		if (typing) {
			commitTyping();
		}
		const list = [...marks];
		if (drafting) {
			list.push(drafting);
		}
		if (!image || !naturalWidth || !naturalHeight) {
			oncancel();
			return;
		}
		const flat = document.createElement('canvas');
		flat.width = naturalWidth;
		flat.height = naturalHeight;
		const context = flat.getContext('2d');
		if (!context) {
			oncancel();
			return;
		}
		paint(context, list);
		onconfirm(stripDataUrl(flat.toDataURL('image/png')));
	}

	const TOOLS: { id: Tool; label: string; icon: typeof PencilIcon }[] = [
		{ id: 'pen', label: 'Freehand', icon: PencilIcon },
		{ id: 'text', label: 'Text note', icon: TypeIcon },
		{ id: 'arrow', label: 'Arrow — click the start, then the head', icon: ArrowRightIcon },
		{ id: 'eraser', label: 'Erase marks', icon: EraserIcon }
	];

	function pick(next: Tool) {
		if (typing) {
			commitTyping();
		}
		arrowStart = null;
		tool = next;
	}
</script>

<!-- Opaque, full-surface and above everything: while an image is being marked up it is the only thing
     to look at, and the conversation behind it must not show through the drawing. -->
<div class="fixed inset-0 z-50 flex flex-col gap-3 bg-[var(--neu-bg)] p-4">
	<div class="flex items-baseline gap-2">
		<h2 class="text-sm font-semibold">{label}</h2>
		<p class="text-muted-foreground text-xs">
			{#if tool === 'arrow'}
				{arrowStart ? 'Click again to place the arrow head.' : 'Click where the arrow starts.'}
			{:else if tool === 'text'}
				{typing
					? 'Type the note — Enter places it, Escape drops it.'
					: 'Click where the note goes, then type.'}
			{:else if tool === 'eraser'}
				Drag over your marks to rub them out — the picture underneath stays.
			{:else}
				Drag to draw.
			{/if}
		</p>
	</div>

	<div class="flex min-h-0 flex-1 gap-4">
		<!-- The rail, in the tools' own order: what draws, what it draws with, what takes it back,
		     then what ends the session. -->
		<div class="flex w-16 shrink-0 flex-col items-center gap-2 overflow-y-auto py-1">
			{#each TOOLS as item (item.id)}
				{@const Icon = item.icon}
				<button
					type="button"
					title={item.label}
					aria-label={item.label}
					aria-pressed={tool === item.id}
					onclick={() => pick(item.id)}
					class="neu-btn flex size-9 shrink-0 items-center justify-center rounded-lg"
				>
					<Icon class="size-4" />
				</button>
			{/each}

			<div class="neu-well mt-2 grid grid-cols-3 gap-0.5 rounded-md p-1">
				{#each PALETTE as row, r (r)}
					{#each row as swatch (swatch)}
						<button
							type="button"
							title={swatch}
							aria-label={`Colour ${swatch}`}
							aria-pressed={colour === swatch}
							onclick={() => (colour = swatch)}
							style={`background:${swatch}`}
							class={`size-4 rounded-[2px] ${colour === swatch ? 'ring-2 ring-[var(--neu-accent)]' : ''}`}
						></button>
					{/each}
				{/each}
			</div>

			<button
				type="button"
				title="Undo"
				aria-label="Undo"
				disabled={!canUndo}
				onclick={undo}
				class="neu-btn mt-2 flex size-9 shrink-0 items-center justify-center rounded-lg disabled:opacity-40"
			>
				<RotateCcwIcon class="size-4" />
			</button>
			<button
				type="button"
				title="Redo"
				aria-label="Redo"
				disabled={!canRedo}
				onclick={redo}
				class="neu-btn flex size-9 shrink-0 items-center justify-center rounded-lg disabled:opacity-40"
			>
				<RotateCwIcon class="size-4" />
			</button>

			<button
				type="button"
				title="Cancel — discard the mark-up"
				aria-label="Cancel"
				onclick={oncancel}
				class="neu-btn text-muted-foreground hover:text-foreground mt-auto flex size-9 shrink-0 items-center justify-center rounded-lg"
			>
				<XIcon class="size-4" />
			</button>
			<button
				type="button"
				title="Confirm — keep the mark-up"
				aria-label="Confirm"
				onclick={confirm}
				class="neu-btn flex size-9 shrink-0 items-center justify-center rounded-lg text-[var(--neu-accent)]"
			>
				<CheckIcon class="size-4" />
			</button>
		</div>

		<!-- The picture sits in a well, letterboxed. bind:clientWidth/Height is what the canvas is
		     fitted to, so a window resize re-scales it without touching a single mark. -->
		<div
			bind:clientWidth={frameWidth}
			bind:clientHeight={frameHeight}
			class="neu-well relative flex min-w-0 flex-1 items-center justify-center overflow-hidden rounded-xl"
		>
			<canvas
				bind:this={canvas}
				width={naturalWidth}
				height={naturalHeight}
				style={`width:${displayWidth}px;height:${displayHeight}px`}
				onpointerdown={onPointerDown}
				onpointermove={onPointerMove}
				onpointerup={onPointerUp}
				onpointercancel={onPointerUp}
				class={`touch-none rounded-md ${tool === 'text' ? 'cursor-text' : 'cursor-crosshair'}`}
			></canvas>
		</div>
	</div>
</div>
