<script lang="ts">
	import { onMount } from 'svelte';
	import Conversation from '$lib/components/ai-elements/conversation/conversation.svelte';
	import ConversationContent from '$lib/components/ai-elements/conversation/conversation-content.svelte';
	import ConversationScrollButton from '$lib/components/ai-elements/conversation/conversation-scroll-button.svelte';
	import Message from '$lib/components/ai-elements/message/core/message.svelte';
	import MessageContent from '$lib/components/ai-elements/message/core/message-content.svelte';
	import Response from '$lib/components/ai-elements/response/response.svelte';
	import Image from '$lib/components/ai-elements/image/image.svelte';
	import AssistantTurnGroup from '$lib/chat/AssistantTurnGroup.svelte';
	import FeedbackTurn from '$lib/chat/FeedbackTurn.svelte';
	import Composer from '$lib/chat/Composer.svelte';
	import ImageEditor from '$lib/chat/ImageEditor.svelte';
	import Setup from '$lib/chat/Setup.svelte';
	import Preset from '$lib/chat/Preset.svelte';
	import Grounding from '$lib/chat/Grounding.svelte';
	import ManualDefinition from '$lib/chat/ManualDefinition.svelte';
	import ConnectOptions from '$lib/chat/ConnectOptions.svelte';
	import {
		DropdownMenu,
		DropdownMenuTrigger,
		DropdownMenuContent,
		DropdownMenuItem,
		DropdownMenuSeparator
	} from '$lib/components/ui/dropdown-menu/index.js';
	import { Button } from '$lib/components/ui/button/index.js';
	import MenuIcon from '@lucide/svelte/icons/menu';
	import Trash2Icon from '@lucide/svelte/icons/trash-2';
	import ImagePlusIcon from '@lucide/svelte/icons/image-plus';
	import Axis3dIcon from '@lucide/svelte/icons/axis-3d';
	import CameraIcon from '@lucide/svelte/icons/camera';
	import DownloadIcon from '@lucide/svelte/icons/download';
	import ActivityIcon from '@lucide/svelte/icons/activity';
	import ArrowUpIcon from '@lucide/svelte/icons/arrow-up';
	import OctagonIcon from '@lucide/svelte/icons/octagon';
	import HouseIcon from '@lucide/svelte/icons/house';
	import { getProvider } from '$lib/chat/providers';
	import { cn } from '$lib/utils';
	import type {
		ClusterInfo,
		ClusterSelectionPayload,
		ComponentTabInfo,
		GroundingCategory,
		GroundingSelectionPayload,
		PythonFunctionInfo,
		ReferencedGeometryInfo,
		SetupResult,
		SnapshotKind,
		UiImage,
		SnapshotMessagePayload,
		SubmitMessage,
		ToolsSelectionPayload,
		UiChat,
		UiMessage,
		UiPreset,
		UiState,
		UnitsOverridePayload
	} from '$lib/bridge';

	const BRIDGE_SCHEME = 'phbridge';

	let messages = $state<UiMessage[]>([]);
	let stream = $state<string | null>(null);
	let connected = $state(false);
	let busy = $state(false);
	let needsSetup = $state(false);
	// The window is on Home (the entry screen) rather than viewing a Chat. Only Home offers the
	// placement options; a Chat still awaiting its Conversation Log shows the logo alone.
	let home = $state(false);
	let status = $state('');
	let configuredProviders = $state<string[]>([]);
	// Grounding state for the grounding panel: whether a component catalog is wired (enables the
	// Composer's grounding button), the available tab → panels tree, and the current selection
	// (null = include everything).
	let groundingWired = $state(false);
	let groundingTree = $state<GroundingCategory[]>([]);
	let groundingSelection = $state<GroundingCategory[] | null>(null);
	// Whether the component grounding folds typed signatures into the prompt instead of bare names.
	let exposeSignatures = $state(false);
	// Grounded components grouped by tab, for the "/c/<tab>/<component>" staged autocomplete.
	let availableComponents = $state<ComponentTabInfo[]>([]);

	// Cluster grounding state: whether a cluster grounding is wired, the available clusters (from
	// Files/CLUSTERS), and the current selection (null = include everything).
	let clustersWired = $state(false);
	let availableClusters = $state<ClusterInfo[]>([]);
	let clusterSelection = $state<string[] | null>(null);

	// Tools grounding state: whether a tools grounding is wired, the tools on the canvas (for the Tools
	// page + "/t/" autocomplete), and the current selection (null = include everything).
	let toolsWired = $state(false);
	let availableTools = $state<string[]>([]);
	let toolsSelection = $state<string[] | null>(null);

	// Referenced Rhino Geometry and Python grounding state (read-only pages).
	let referencedGeometryWired = $state(false);
	let availableReferencedGeometry = $state<ReferencedGeometryInfo[]>([]);
	let pythonWired = $state(false);
	let pythonFunctions = $state<PythonFunctionInfo[]>([]);

	// Document-units grounding state: whether a units grounding is wired, the live document units, the
	// current override (null = use the document units), and the dropdown choices.
	let unitsWired = $state(false);
	let documentUnits = $state('');
	let unitsOverride = $state<string | null>(null);
	let unitOptions = $state<string[]>([]);

	// Human-tool state: whether a Geometry Snapshot tool is wired, whether generated geometry is
	// present right now (both together light the composer's geometry indicator), whether pressing the
	// button sends the snapshot as its own message or attaches it to the prompt box, the tool's default
	// message, the current override (null = use the default), and whether an Add Image tool is
	// wired (without it image intake is fully disabled in the composer).
	let snapshotWired = $state(false);
	let snapshotGeometryPresent = $state(false);
	let snapshotSendsMessage = $state(true);
	let snapshotDefaultMessage = $state('');
	let snapshotMessage = $state<string | null>(null);
	let imageToolWired = $state(false);

	// View-snapshot state: the same shape minus any armed flag — a view capture needs nothing on the
	// canvas and moves no camera, so its button is live from the moment the tool is wired.
	let viewSnapshotWired = $state(false);
	let viewSnapshotSendsMessage = $state(true);
	let viewSnapshotDefaultMessage = $state('');
	let viewSnapshotMessage = $state<string | null>(null);

	// Marker human tools — nothing to configure, each just adds its header button: an export that
	// writes this conversation to a .txt transcript, and a door onto the session's signal trace.
	let exportToolWired = $state(false);
	let signalTraceToolWired = $state(false);

	// The Image Mark Up tool adds no button of its own — it puts the image editor in front of every
	// image the human sends. `markUp` is that editor's whole state: the image being drawn on plus where
	// the result must go when it is confirmed. Null = the editor is closed.
	let markUpToolWired = $state(false);
	let markUp = $state<{
		base64: string;
		mediaType: string;
		label: string;
		/** Where the confirmed image goes. A capture on its way to the prompt box (`attach`) still lands
		 *  there if the human cancels — only the mark-up is discarded. A capture in send mode (`send`) has
		 *  no plain fallback, so cancelling abandons it. `pending` is an image already in the strip. */
		target:
			| { kind: 'pending'; id: number }
			| { kind: 'attach'; lane: 'snapshot' | 'viewsnapshot' }
			| { kind: 'send'; snapshot: SnapshotKind };
	} | null>(null);

	// The grounding button opens the panel whenever any grounding kind — or human tool — is wired.
	let groundingAvailable = $derived(
		groundingWired ||
			clustersWired ||
			toolsWired ||
			referencedGeometryWired ||
			pythonWired ||
			unitsWired ||
			snapshotWired ||
			viewSnapshotWired ||
			imageToolWired ||
			exportToolWired ||
			signalTraceToolWired ||
			markUpToolWired
	);

	// The cluster names currently exposed to the model (selection applied), for the "/c/" autocomplete.
	let includedClusterNames = $derived(
		availableClusters
			.map((c) => c.name)
			.filter((n) => clusterSelection === null || clusterSelection.includes(n))
	);

	// The tool names currently exposed to the model (selection applied), for the "/t/" autocomplete.
	let includedToolNames = $derived(
		availableTools.filter((n) => toolsSelection === null || toolsSelection.includes(n))
	);

	// Setup screen state. `needsSetup` (from the host) forces it when no provider is configured;
	// `manualSetup` lets the user open it from the header dropdown to add another provider later.
	let manualSetup = $state(false);
	let selectedProviderId = $state<string | null>(null);
	let setupResult = $state<SetupResult | null>(null);

	// Other full-screen pages opened from the header menu (mutually exclusive with the chat view
	// and with setup). null = none open.
	let panel = $state<'preset' | 'manualdef' | 'grounding' | null>(null);
	// Estimated token count from a Token Estimator wired downstream of the viewed ConversationLog,
	// pushed by the host; null = no estimator wired (or no count yet) → the counter hides.
	let tokenCount = $state<number | null>(null);

	// Bundled preset harnesses (from Files/PRESETS), pushed by the host.
	let presets = $state<UiPreset[]>([]);
	// Every Chat on the canvas (the bottom switcher row), pushed by the host.
	let chats = $state<UiChat[]>([]);

	let showSetup = $derived(needsSetup || manualSetup);
	// Only offer "Back to chat" when setup was opened manually (a provider already exists);
	// during first-run setup there is nothing to return to yet.
	let canClose = $derived(!needsSetup);
	// When a key-requiring provider's guide is open, the prompt box captures the key instead.
	let keyProvider = $derived.by(() => {
		if (!showSetup) {
			return null;
		}
		const provider = getProvider(selectedProviderId);
		return provider && provider.needsKey ? { id: provider.id, label: provider.label } : null;
	});

	onMount(() => {
		// C# -> JS surface. The host calls these via ExecuteScript on its UI timer.
		window.physalia = {
			setHistory: (next) => {
				messages = next ?? [];
			},
			setStream: (text) => {
				stream = text;
			},
			setState: (next: UiState) => {
				connected = next.connected;
				busy = next.busy;
				needsSetup = next.needsSetup ?? false;
				home = next.home ?? false;
				status = next.status ?? '';
				configuredProviders = next.configuredProviders ?? [];
				groundingWired = next.groundingWired ?? false;
				groundingTree = next.groundingTree ?? [];
				groundingSelection = next.groundingSelection ?? null;
				exposeSignatures = next.exposeSignatures ?? false;
				availableComponents = next.availableComponents ?? [];
				clustersWired = next.clustersWired ?? false;
				availableClusters = next.availableClusters ?? [];
				clusterSelection = next.clusterSelection ?? null;
				toolsWired = next.toolsWired ?? false;
				availableTools = next.availableTools ?? [];
				toolsSelection = next.toolsSelection ?? null;
				referencedGeometryWired = next.referencedGeometryWired ?? false;
				availableReferencedGeometry = next.availableReferencedGeometry ?? [];
				pythonWired = next.pythonWired ?? false;
				pythonFunctions = next.pythonFunctions ?? [];
				unitsWired = next.unitsWired ?? false;
				documentUnits = next.documentUnits ?? '';
				unitsOverride = next.unitsOverride ?? null;
				unitOptions = next.unitOptions ?? [];
				snapshotWired = next.snapshotWired ?? false;
				snapshotGeometryPresent = next.snapshotGeometryPresent ?? false;
				snapshotSendsMessage = next.snapshotSendsMessage ?? true;
				snapshotDefaultMessage = next.snapshotDefaultMessage ?? '';
				snapshotMessage = next.snapshotMessage ?? null;
				viewSnapshotWired = next.viewSnapshotWired ?? false;
				viewSnapshotSendsMessage = next.viewSnapshotSendsMessage ?? true;
				viewSnapshotDefaultMessage = next.viewSnapshotDefaultMessage ?? '';
				viewSnapshotMessage = next.viewSnapshotMessage ?? null;
				imageToolWired = next.imageToolWired ?? false;
				exportToolWired = next.exportToolWired ?? false;
				signalTraceToolWired = next.signalTraceToolWired ?? false;
				markUpToolWired = next.markUpToolWired ?? false;
			},
			setSetupResult: (result) => {
				setupResult = result;
			},
			setTokenCount: (count) => {
				tokenCount = count;
			},
			setPresets: (next) => {
				presets = next ?? [];
			},
			setChats: (next) => {
				chats = next ?? [];
			},
			attachSnapshot: (image) => {
				// Attach mode: the host captured a snapshot and hands it here instead of sending it —
				// it joins the composer's attachment strip and rides the message the user types. With an
				// Image Mark Up tool wired it stops at the editor on the way.
				attachCapture(image, 'snapshot', 'Geometry snapshot');
			},
			attachViewSnapshot: (image) => {
				// The view button's attach mode — its own lane in the composer, same treatment.
				attachCapture(image, 'viewsnapshot', 'View snapshot');
			},
			markUpSnapshot: (image, kind) => {
				// A send-mode capture, held back for mark-up: nothing has been sent yet, and nothing will
				// be unless the human confirms (see confirmMarkUp / cancelMarkUp).
				if (!image?.base64) {
					return;
				}
				markUp = {
					base64: image.base64,
					mediaType: image.mediaType ?? 'image/png',
					label: kind === 'geometry-snapshot' ? 'Geometry snapshot' : 'View snapshot',
					target: { kind: 'send', snapshot: kind }
				};
			}
		};

		// C# pulls the pending outgoing message (text + images) from here after it
		// intercepts the phbridge://submit navigation.
		window.__physaliaTake = () => {
			const pending = window.__physaliaPending ?? '';
			window.__physaliaPending = '';
			return pending;
		};

		return () => {
			delete window.physalia;
			delete window.__physaliaTake;
		};
	});

	// JS -> C#: poke the host with a custom-URI navigation it cancels.
	//  - Text-only: carried directly in the URL query (small, and the proven path).
	//  - With images: the full payload is stashed on window (base64 blows past URL
	//    limits) and the host pulls it back with __physaliaTake().
	function send(message: SubmitMessage) {
		if (message.images.length === 0) {
			window.location.href = `${BRIDGE_SCHEME}://submit?text=${encodeURIComponent(message.text)}`;
			return;
		}

		const json = JSON.stringify(message);

		// Primary (WebView2): hand the payload to the host via the message channel. Reliable for
		// large payloads, unlike pulling the JSON back after a cancelled navigation (that runs
		// in a transient context and returns null).
		const webview = (window as unknown as { chrome?: { webview?: { postMessage?: (m: string) => void } } })
			.chrome?.webview;
		if (webview?.postMessage) {
			webview.postMessage(json);
			return;
		}

		// Fallback (non-WebView2, e.g. Mac WKWebView): stash + navigate; host pulls it back.
		window.__physaliaPending = json;
		window.location.href = `${BRIDGE_SCHEME}://submit?images=1`;
	}

	// A capture bound for the prompt box. With an Image Mark Up tool wired it opens in the editor
	// first; otherwise it goes straight into the strip. `lane` is the composer lane the granting tool
	// owns, so a capture is still revoked by its own tool after being drawn on.
	function attachCapture(
		image: UiImage | undefined,
		lane: 'snapshot' | 'viewsnapshot',
		label: string
	) {
		const base64 = image?.base64 ?? '';
		const mediaType = image?.mediaType ?? 'image/png';
		if (!base64) {
			return;
		}
		if (markUpToolWired) {
			markUp = { base64, mediaType, label, target: { kind: 'attach', lane } };
			return;
		}
		putInStrip(base64, mediaType, lane);
	}

	// Hand an image to the composer on the lane its tool granted. The lane is not cosmetic: it decides
	// which tool being unwired takes the attachment back down with it.
	function putInStrip(base64: string, mediaType: string, lane: 'snapshot' | 'viewsnapshot') {
		if (lane === 'snapshot') {
			void composer?.addSnapshot(base64, mediaType);
		} else {
			void composer?.addViewSnapshot(base64, mediaType);
		}
	}

	// Open the image editor on an image already in the prompt box (its thumbnail's edit button).
	function editPendingImage(image: { id: number; base64: string; mediaType: string }) {
		markUp = {
			base64: image.base64,
			mediaType: image.mediaType,
			label: 'Attached image',
			target: { kind: 'pending', id: image.id }
		};
	}

	// Confirm: the mark-up is flattened into `base64` and the image goes where it was always going.
	function confirmMarkUp(base64: string) {
		const open = markUp;
		markUp = null;
		if (!open || !base64) {
			return;
		}
		if (open.target.kind === 'pending') {
			composer?.replaceImage(open.target.id, base64);
			return;
		}
		if (open.target.kind === 'attach') {
			putInStrip(base64, 'image/png', open.target.lane);
			return;
		}
		// Send mode: this IS the send. The text is left empty deliberately — the message that speaks for
		// the snapshot is read from the wired tool host-side, never carried by the page.
		send({
			text: '',
			images: [{ base64, mediaType: 'image/png', filename: `${open.target.snapshot}.png` }],
			kind: open.target.snapshot
		});
	}

	// Cancel: the mark-up is discarded. What happens to the IMAGE depends on where it was headed — an
	// attachment or an already-attached image survives untouched (cancel means "not this drawing", not
	// "not this picture"), but a send-mode capture has no plain fallback to fall back to: it was never
	// attached anywhere, so abandoning the mark-up abandons the capture.
	function cancelMarkUp() {
		const open = markUp;
		markUp = null;
		if (open?.target.kind === 'attach') {
			putInStrip(open.base64, open.mediaType, open.target.lane);
		}
	}

	// Ask the host to cancel the active inference on the wired pipeline's LLM Call. The composer's
	// cancel button is enabled only while `busy`, so this fires only when a request is in flight.
	function cancel() {
		window.location.href = `${BRIDGE_SCHEME}://cancel`;
	}

	// Open an external setup link in the system browser (the host cancels the nav and shells out).
	function openLink(url: string) {
		window.location.href = `${BRIDGE_SCHEME}://open?url=${encodeURIComponent(url)}`;
	}

	// Ask the host to drop an empty harness — a Chat and nothing else — onto the canvas, and switch
	// this window to it. Repeatable: a document can carry any number of harnesses, so this stays
	// available from the header menu long after the first one is placed.
	function placeEmptyHarness() {
		window.location.href = `${BRIDGE_SCHEME}://placeemptyharness`;
		panel = null;
	}

	// Ask the host to place the chosen bundled preset (.gh) as a NEW harness beside whatever is
	// already on the canvas, switch this window to the Chat inside it, then return to chat.
	function placePreset(file: string) {
		window.location.href = `${BRIDGE_SCHEME}://placepreset?file=${encodeURIComponent(file)}`;
		panel = null;
	}

	// Ask the host to clear signals / conversations / histories from every Physalia component in
	// the open document.
	function clearAllComponents() {
		window.location.href = `${BRIDGE_SCHEME}://clearall`;
	}

	// Switch the window to another switcher entry: a Chat component (its conversation log history, or
	// the default screen when it has none), or Home. The next state tick re-pushes what to show.
	//
	// Any open page is closed on the way. Picking an entry is a request to LOOK at something, and the
	// preset gallery / manual-definition / setup pages all render in front of the conversation — Home
	// especially would otherwise appear to do nothing while the gallery that led there stayed up.
	function selectChat(entry: UiChat) {
		panel = null;
		manualSetup = false;
		selectedProviderId = null;
		setupResult = null;

		// Position as well as guid: two Chats can share an InstanceGuid, so the guid alone cannot say
		// which circle was clicked. The host resolves by position and cross-checks the guid.
		window.location.href =
			`${BRIDGE_SCHEME}://selectchat?id=${encodeURIComponent(entry.id)}&ordinal=${entry.ordinal}`;
	}

	// Apply a grounding selection to the wired ConversationLog. The payload {all, leaves} is small enough to
	// carry in the URL query (unlike image payloads). all=true returns to include-everything.
	function setGrounding(payload: GroundingSelectionPayload) {
		const json = JSON.stringify(payload);
		window.location.href = `${BRIDGE_SCHEME}://setgrounding?sel=${encodeURIComponent(json)}`;
	}

	// Toggle typed component signatures in the grounded system prompt (instead of bare names).
	function setSignatures(on: boolean) {
		window.location.href = `${BRIDGE_SCHEME}://setsignatures?on=${on ? '1' : '0'}`;
	}

	// Apply a cluster selection to the wired ConversationLog. all=true returns to include-everything.
	function setClusters(payload: ClusterSelectionPayload) {
		const json = JSON.stringify(payload);
		window.location.href = `${BRIDGE_SCHEME}://setclusters?sel=${encodeURIComponent(json)}`;
	}

	// Apply a tools selection to the wired ConversationLog. all=true returns to include-every-present-tool.
	function setTools(payload: ToolsSelectionPayload) {
		const json = JSON.stringify(payload);
		window.location.href = `${BRIDGE_SCHEME}://settools?sel=${encodeURIComponent(json)}`;
	}

	// Apply a document-units override to the wired ConversationLog. reset=true returns to the live doc units.
	function setUnits(payload: UnitsOverridePayload) {
		const json = JSON.stringify(payload);
		window.location.href = `${BRIDGE_SCHEME}://setunits?sel=${encodeURIComponent(json)}`;
	}

	// Apply a geometry-snapshot message override to the wired ConversationLog. reset=true returns to
	// the grounding's default message.
	function setSnapshotMessage(payload: SnapshotMessagePayload) {
		const json = JSON.stringify(payload);
		window.location.href = `${BRIDGE_SCHEME}://setsnapshotmessage?sel=${encodeURIComponent(json)}`;
	}

	// Switch the wired Geometry Snapshot tool between sending its snapshot as its own message and
	// attaching it to the prompt box. The flag lives on the component (its context menu shows the same
	// state), so the new value arrives back on the next state push rather than being held here.
	function setSnapshotSends(on: boolean) {
		window.location.href = `${BRIDGE_SCHEME}://setsnapshotsends?on=${on ? '1' : '0'}`;
	}

	// Ask the host to capture a viewport snapshot of the generated geometry. Fired by the composer's
	// geometry button, whose terminus is the tool's choice: send it straight off as its own user
	// message carrying the predefined message, or attach it to the prompt box (the host pushes the
	// image back through window.physalia.attachSnapshot) for the user to caption themselves.
	function sendSnapshot() {
		if (snapshotSendsMessage && markUpToolWired) {
			// Mark-up first: the host captures and hands the image back rather than sending it, and it
			// only leaves if the editor is confirmed.
			window.location.href = `${BRIDGE_SCHEME}://marksnapshot`;
			return;
		}
		window.location.href = snapshotSendsMessage
			? `${BRIDGE_SCHEME}://sendsnapshot`
			: `${BRIDGE_SCHEME}://attachsnapshot`;
	}

	// Apply a view-snapshot message override to the wired ConversationLog — the view-snapshot twin of
	// setSnapshotMessage, same payload under its own verb.
	function setViewSnapshotMessage(payload: SnapshotMessagePayload) {
		const json = JSON.stringify(payload);
		window.location.href = `${BRIDGE_SCHEME}://setviewsnapshotmessage?sel=${encodeURIComponent(json)}`;
	}

	// Switch the wired View Snapshot tool between sending its capture as its own message and attaching it
	// to the prompt box. Like the geometry twin, the flag lives on the component.
	function setViewSnapshotSends(on: boolean) {
		window.location.href = `${BRIDGE_SCHEME}://setviewsnapshotsends?on=${on ? '1' : '0'}`;
	}

	// Ask the host to capture the active viewport as-is. Fired by the view button; same two termini as
	// the geometry button — sent straight off with its predefined message, or pushed back through
	// window.physalia.attachViewSnapshot for the user to caption.
	function sendViewSnapshot() {
		if (viewSnapshotSendsMessage && markUpToolWired) {
			window.location.href = `${BRIDGE_SCHEME}://markviewsnapshot`;
			return;
		}
		window.location.href = viewSnapshotSendsMessage
			? `${BRIDGE_SCHEME}://sendviewsnapshot`
			: `${BRIDGE_SCHEME}://attachviewsnapshot`;
	}

	// Ask the host to write the viewed conversation to a plain-text transcript (it owns the save
	// dialog). Fired by the header's export button, shown only while an Export Conversation tool
	// is wired.
	function exportConversation() {
		window.location.href = `${BRIDGE_SCHEME}://exportconversation`;
	}

	// Ask the host to open the signal-trace window. Fired by the header's trace button, shown only
	// while a Signal Trace tool is wired.
	function openSignalTrace() {
		window.location.href = `${BRIDGE_SCHEME}://opensignaltrace`;
	}

	// Hand a pasted API key to the host, which writes it to API_KEY_CONFIG.YAML and reports back
	// via setSetupResult. encodeURIComponent keeps the key intact in the URL (no literal '+').
	function saveKey(providerId: string, key: string) {
		window.location.href =
			`${BRIDGE_SCHEME}://savekey?provider=${encodeURIComponent(providerId)}&key=${encodeURIComponent(key)}`;
	}

	function selectProvider(id: string | null) {
		selectedProviderId = id;
		setupResult = null; // a fresh page shouldn't carry the previous provider's result
	}

	function openSetup() {
		manualSetup = true;
		panel = null;
		selectedProviderId = null;
		setupResult = null;
	}

	function closeSetup() {
		manualSetup = false;
		selectedProviderId = null;
		setupResult = null;
	}

	// Open one of the pages (preset / manual definition / grounding), leaving setup.
	function openPanel(which: 'preset' | 'manualdef' | 'grounding') {
		panel = which;
		manualSetup = false;
		selectedProviderId = null;
		setupResult = null;
	}

	function closePanel() {
		panel = null;
	}

	// The composer instance, for the rail buttons that drive it from outside the box: the submit
	// arrow calls its exported submit(), the Add Image human tool its exported openPicker(), a
	// host-captured snapshot in attach mode its exported addSnapshot() / addViewSnapshot(), and the
	// image editor its exported replaceImage() once a marked-up attachment comes back.
	let composer = $state<{
		submit: () => void;
		openPicker: () => void;
		replaceImage: (id: number, base64: string) => void;
		addSnapshot: (base64: string, mediaType: string) => Promise<void>;
		addViewSnapshot: (base64: string, mediaType: string) => Promise<void>;
	} | null>(null);

	// Mirror of the Composer's own inert gate (busy / setup / disconnected, except in API-key
	// mode), so the external submit button greys out exactly when the box itself is inert.
	let composerInert = $derived(busy || ((showSetup || !connected) && !keyProvider));

	// The geometry button arms only when a Geometry Snapshot tool is wired AND generated
	// geometry exists on the canvas right now.
	let snapshotArmed = $derived(snapshotWired && snapshotGeometryPresent);

	let isEmpty = $derived(messages.length === 0 && !stream);
	// Provider configured (not setup) but no ConversationLog wired and nothing said yet: offer the
	// connect-a-conversation log / workflow / configure options instead of a bare empty conversation.
	let showConnect = $derived(!showSetup && !connected && isEmpty);

	// Everything that is NOT a conversation: Home / the empty-harness logo, the first-run and manual
	// setup screens, and the header pages. They share one scroller of their own — see the markup for
	// why they must not live inside the Conversation's.
	let staticSurface = $derived(showSetup || panel !== null || showConnect);

	// Group the flat message list into render units: each user message stands alone, while
	// a run of consecutive assistant messages (the agentic rounds for one prompt) collapses
	// into a single assistant turn. The live `stream` partial is appended to the active
	// assistant turn (or starts one) so the in-progress round renders inside the same group.
	type RenderGroup =
		| { kind: 'user'; key: string; message: UiMessage }
		| { kind: 'assistant'; key: string; messages: UiMessage[]; streaming: boolean };

	let groups = $derived.by<RenderGroup[]>(() => {
		const out: RenderGroup[] = [];
		let current: Extract<RenderGroup, { kind: 'assistant' }> | null = null;

		for (const message of messages) {
			if (message.role === 'assistant') {
				if (!current) {
					current = { kind: 'assistant', key: message.id, messages: [], streaming: false };
					out.push(current);
				}
				current.messages.push(message);
			} else {
				current = null;
				out.push({ kind: 'user', key: message.id, message });
			}
		}

		if (stream) {
			if (!current) {
				current = { kind: 'assistant', key: 'live', messages: [], streaming: false };
				out.push(current);
			}
			current.messages.push({ id: 'live', role: 'assistant', text: stream, tools: [] });
			current.streaming = true;
		}

		return out;
	});
</script>

<main class="bg-transparent text-foreground relative flex h-screen flex-col overflow-hidden">
	<!-- Header row: the menu, then the human tools spreading rightwards across the top — each
	     appears as it is wired into the Conversation Log's Human Tools input. -->
	<header class="flex shrink-0 items-center gap-3 px-3 py-2">
		<DropdownMenu>
			<DropdownMenuTrigger>
				{#snippet child({ props })}
					<Button variant="outline" size="icon-lg" title="Menu" {...props}>
						<MenuIcon class="size-4" />
					</Button>
				{/snippet}
			</DropdownMenuTrigger>
			<DropdownMenuContent align="start" class="w-60">
				<DropdownMenuItem class="whitespace-nowrap" onSelect={openSetup}>
					Set up providers…
				</DropdownMenuItem>
				<DropdownMenuSeparator />
				<DropdownMenuItem
					class="whitespace-nowrap"
					disabled={!groundingAvailable}
					onSelect={() => openPanel('grounding')}
				>
					Grounding & tool options…
				</DropdownMenuItem>
				<DropdownMenuSeparator />
				<DropdownMenuItem class="whitespace-nowrap" onSelect={() => openPanel('preset')}>
					Add preset
				</DropdownMenuItem>
				<DropdownMenuItem class="whitespace-nowrap" onSelect={placeEmptyHarness}>
					Add empty harness
				</DropdownMenuItem>
				<DropdownMenuSeparator />
				<DropdownMenuItem class="whitespace-nowrap" onSelect={() => openPanel('manualdef')}>
					Add new manual definition
				</DropdownMenuItem>
			</DropdownMenuContent>
		</DropdownMenu>

		{#if imageToolWired}
			<!-- Add-image button: appears only while an Add Image human tool is wired into the
			     Conversation Log's Human Tools input — without it, image intake does not exist. -->
			<Button
				variant="outline"
				size="icon-lg"
				onclick={() => composer?.openPicker()}
				disabled={composerInert || !!keyProvider}
				title="Add image"
			>
				<ImagePlusIcon class="size-4" />
			</Button>
		{/if}

		{#if snapshotWired}
			<!-- Geometry-snapshot button: appears the moment a Geometry Snapshot human tool is
			     wired, but stays greyed until a transmitter has generated geometry (snapshotArmed).
			     Pressing it captures a viewport snapshot; where that snapshot goes is the tool's
			     choice (its "Send With Default Message" toggle). On: it is sent immediately as its own
			     message carrying the predefined message, edited in the grounding panel's Geometry
			     Snapshot page — nothing is ever attached to a typed prompt behind the user's back.
			     Off: it lands in the prompt box like a pasted image, for the user to caption. -->
			<Button
				variant="outline"
				size="icon-lg"
				class={snapshotArmed ? 'text-[var(--neu-accent)]' : ''}
				onclick={sendSnapshot}
				disabled={composerInert || !!keyProvider || !snapshotArmed}
				title={!snapshotArmed
					? 'Geometry snapshot — enabled once generated geometry is on the canvas'
					: snapshotSendsMessage
						? 'Send a viewport snapshot of the generated geometry with its predefined message'
						: 'Attach a viewport snapshot of the generated geometry to your message'}
			>
				<Axis3dIcon class="size-4" />
			</Button>
		{/if}

		{#if viewSnapshotWired}
			<!-- View button: appears while a View Snapshot human tool is wired, and unlike the geometry
			     button it is live immediately — a view capture needs nothing on the canvas and never
			     moves the camera, so there is no armed state to wait for. Where the capture goes is the
			     tool's "Send With Default Message" choice: sent as its own message with the predefined
			     text (edited on the View Snapshot page), or attached to the prompt box for the user to
			     caption. Nothing is ever attached to a typed prompt behind the user's back. -->
			<Button
				variant="outline"
				size="icon-lg"
				class="text-[var(--neu-accent)]"
				onclick={sendViewSnapshot}
				disabled={composerInert || !!keyProvider}
				title={viewSnapshotSendsMessage
					? 'Send a snapshot of the current Rhino view with its predefined message'
					: 'Attach a snapshot of the current Rhino view to your message'}
			>
				<CameraIcon class="size-4" />
			</Button>
		{/if}

		{#if exportToolWired}
			<!-- Export button: appears while an Export Conversation human tool is wired. Pressing it
			     asks the host to save this conversation as a plain-text transcript (it owns the save
			     dialog). Unlike the send-a-message tools this reads what has already happened, so it
			     stays live while the pipeline is busy or a provider is still being set up. -->
			<Button variant="outline" size="icon-lg" onclick={exportConversation} title="Export conversation (.txt)">
				<DownloadIcon class="size-4" />
			</Button>
		{/if}

		{#if signalTraceToolWired}
			<!-- Signal-trace button: appears while a Signal Trace human tool is wired, and opens the
			     session's trace window. Also live while busy — watching signals mid-run is the point. -->
			<Button variant="outline" size="icon-lg" onclick={openSignalTrace} title="Open the signal trace">
				<ActivityIcon class="size-4" />
			</Button>
		{/if}
	</header>

	<!-- flex-1 + min-h-0 lets this region size to the space left by the composer and
	     shrink, so the composer stays pinned at the bottom and the chat scrolls within.
	     The chat-scroll scrollbar (app.css) is as wide as the action-stack buttons below
	     (36px), so pr-3 lines its centre up with theirs: both sit 30px in from the window
	     edge (12px margin + half of 36px). -->
	<div class="relative min-h-0 flex-1 pr-3">
		{#if staticSurface}
			<!-- Static surfaces (Home, setup, the header pages) get their OWN scroller, deliberately
			     outside <Conversation>. They are not conversations: each leads with a logo or a
			     heading and must open at the top. Inside the Conversation they could not — its
			     stick-to-bottom observer jumps to the bottom whenever content changes while it
			     believes it is at the bottom, which it does the instant a surface is swapped in, so
			     Home would open already scrolled past its own logo in a short window. A separate
			     element sidesteps the fight entirely: a freshly mounted scroller starts at the top,
			     and switching back remounts the Conversation, which correctly jumps to the latest
			     message. Classes mirror ConversationContent's own (flex flex-col gap-8 p-4) so the
			     surfaces sit exactly where they used to. -->
			<div class="chat-scroll flex h-full flex-col gap-8 overflow-x-hidden overflow-y-scroll p-4">
			{#if showSetup}
				<Setup
					selectedId={selectedProviderId}
					{setupResult}
					{canClose}
					{configuredProviders}
					onselect={selectProvider}
					onopenlink={openLink}
					onclose={closeSetup}
				/>
			{:else if panel === 'preset'}
				<Preset {presets} onplace={placePreset} onclose={closePanel} />
			{:else if panel === 'grounding'}
				<Grounding
					tree={groundingTree}
					selection={groundingSelection}
					{exposeSignatures}
					clusters={availableClusters}
					clusterSelection={clusterSelection}
					tools={availableTools}
					{toolsSelection}
					referencedGeometry={availableReferencedGeometry}
					{pythonFunctions}
					{unitsWired}
					{documentUnits}
					{unitsOverride}
					{unitOptions}
					{snapshotWired}
					{snapshotSendsMessage}
					{snapshotDefaultMessage}
					{snapshotMessage}
					{viewSnapshotWired}
					{viewSnapshotSendsMessage}
					{viewSnapshotDefaultMessage}
					{viewSnapshotMessage}
					{imageToolWired}
					{exportToolWired}
					{signalTraceToolWired}
					{markUpToolWired}
					onapply={setGrounding}
					onapplysignatures={setSignatures}
					onapplyclusters={setClusters}
					onapplytools={setTools}
					onapplyunits={setUnits}
					onapplysnapshot={setSnapshotMessage}
					onapplysnapshotsends={setSnapshotSends}
					onapplyviewsnapshot={setViewSnapshotMessage}
					onapplyviewsnapshotsends={setViewSnapshotSends}
					onclose={closePanel}
				/>
			{:else if panel === 'manualdef'}
				<ManualDefinition onclose={closePanel} />
			{:else}
				<ConnectOptions
						onpreset={() => openPanel('preset')}
						onemptyharness={placeEmptyHarness}
						onconfigure={openSetup}
						{home}
					/>
			{/if}
			</div>
		{:else}
		<Conversation class="h-full">
			<!-- overflow-y-scroll (not auto) keeps the recessed scrollbar channel (.chat-scroll,
			     app.css) always visible; the thumb only appears when there is something to scroll.
			     overflow-x-hidden: a horizontal scrollbar must never appear — overlong unbreakable
			     content clips instead (and bubble text breaks/hyphenates, see the user Message). -->
			<ConversationContent class="chat-scroll min-h-0 flex-1 overflow-x-hidden overflow-y-scroll">
			{#if isEmpty}
				<div class="text-muted-foreground flex h-full flex-col items-center justify-center gap-1 text-center">
					<p class="text-sm font-medium">Physalia chat</p>
					<p class="text-xs">Send a message to start the conversation.</p>
				</div>
			{/if}

			<!-- The body of a user-side turn. Shared, because a feedback turn renders exactly the same
			     images and bubble — only behind a collapsed header naming the component that spoke. -->
			{#snippet userBody(message: UiMessage)}
				{#if message.images?.length}
					<div class="flex flex-wrap justify-end gap-2">
						{#each message.images as image, i (i)}
							<Image
								base64={image.base64}
								mediaType={image.mediaType}
								alt="attachment"
								class="neu-raised-sm max-h-48 w-auto rounded-lg"
							/>
						{/each}
					</div>
				{/if}
				{#if message.text}
					<MessageContent>
						<!-- break-words + hyphens-auto: unbreakable runs (checksums, ids, URLs)
						     split inside the bubble instead of overflowing it sideways. -->
						<div class="whitespace-pre-wrap break-words hyphens-auto">
							{message.text}
						</div>
					</MessageContent>
				{/if}
			{/snippet}

			{#each groups as group (group.key)}
				{#if group.kind === 'user'}
					<Message from="user" feedback={group.message.feedback}>
						{#if group.message.feedback}
							<FeedbackTurn sources={group.message.sources}>
								{@render userBody(group.message)}
							</FeedbackTurn>
						{:else}
							{@render userBody(group.message)}
						{/if}
					</Message>
				{:else}
					<Message from="assistant">
						<MessageContent>
							<AssistantTurnGroup messages={group.messages} streaming={group.streaming} />
						</MessageContent>
					</Message>
				{/if}
			{/each}
		</ConversationContent>
			<ConversationScrollButton />
		</Conversation>
		{/if}

		<!-- Fade the text out at the scroll area's top and bottom edges instead of clipping it
		     abruptly. Overlay strips rather than a CSS mask — a mask on the scroll container
		     would fade the scrollbar recess with it. They stop 48px short of the right edge
		     (36px channel + the pr-3 inset) so the recess stays crisp, and pointer-events-none
		     keeps the content underneath scrollable and clickable. -->
		<div class="chat-fade-top pointer-events-none absolute top-0 right-12 left-0 h-8"></div>
		<div class="chat-fade-bottom pointer-events-none absolute right-12 bottom-0 left-0 h-8"></div>
	</div>

	<!-- Bottom row: the prompt box with the action stack on its right (clear all components,
	     cancel, submit). items-stretch + justify-between pin the stack's top and bottom buttons
	     to the box's top and bottom edges; the Composer's editor min-height keeps the box at
	     least as tall as the stack. -->
	<div class="flex shrink-0 items-stretch gap-2 px-3 pb-3">
		<div class="flex min-w-0 flex-1 flex-col">
			<Composer
				bind:this={composer}
				disconnected={!connected}
				{busy}
				disabled={showSetup}
				status={showSetup ? '' : status}
				apiKeyProvider={keyProvider}
				{imageToolWired}
				snapshotAttachWired={snapshotWired && !snapshotSendsMessage}
			viewSnapshotAttachWired={viewSnapshotWired && !viewSnapshotSendsMessage}
				{markUpToolWired}
				clusterNames={includedClusterNames}
				toolNames={includedToolNames}
				componentTabs={availableComponents}
				onsend={send}
				onsavekey={saveKey}
				onedit={editPendingImage}
			/>
		</div>

		<!-- gap-3 sets the stack's minimum button spacing to match the top row's horizontal
		     gap-3; justify-between spreads them further only when the prompt box grows taller. -->
		<div class="flex shrink-0 flex-col items-center justify-between gap-3">
			<Button
				variant="outline"
				size="icon-lg"
				title="Clear signals, conversations and histories from every Physalia component in the open document"
				onclick={clearAllComponents}
			>
				<Trash2Icon class="size-4" />
			</Button>

			<Button
				variant="outline"
				size="icon-lg"
				onclick={cancel}
				disabled={!busy}
				title={busy ? 'Cancel the active request' : 'No active request to cancel'}
			>
				<OctagonIcon class="size-4" />
			</Button>

			<Button
				variant="outline"
				size="icon-lg"
				onclick={() => composer?.submit()}
				disabled={composerInert}
				title="Send"
			>
				<ArrowUpIcon class="size-4" />
			</Button>
		</div>
	</div>

	<!-- Switcher row at the very bottom: one emoji per Chat on the canvas — its assigned
	     sea/ocean glyph, matching the component's canvas icon so the two are easy to pair. The
	     active chat sits on a raised accent ring; a chat with no recorded history is dimmed.
	     Clicking an emoji views that Chat's conversation log history (or the default screen when it
	     has none). New emojis appear as Chats are placed.

	     The row is led by Home — a house, not an emoji — which goes back to harness placement and
	     provider setup. The host orders the rest harness by harness, so a rule between two dots means
	     they belong to different harnesses; Home carries a sentinel key, so it is always ruled off
	     from the chats. Within one harness no divider is ever drawn. -->
	{#if chats.length > 0}
		<div class="flex shrink-0 items-center justify-center gap-1 pb-2">
			<!-- Keyed on box.key, never box.id: two Chats can share an InstanceGuid, and a duplicate
			     key would collapse their circles into one. -->
			{#each chats as box, i (box.key)}
				{#if i > 0 && chats[i - 1].harness !== box.harness}
					<span aria-hidden="true" class="bg-muted-foreground/25 mx-1 h-4 w-px shrink-0"></span>
				{/if}
				<button
					type="button"
					onclick={() => selectChat(box)}
					aria-pressed={box.active}
					title={box.home
						? 'Home — place a harness or set up providers'
						: box.active
							? 'Current chat'
							: box.hasHistory
								? 'Switch to this chat (has history)'
								: 'Switch to this chat'}
					class="group flex items-center justify-center rounded-full p-0.5"
				>
					<span
						class={cn(
							'flex size-6 items-center justify-center rounded-full text-sm leading-none transition',
							box.active
								? 'bg-[var(--neu-accent)]/15 shadow-[var(--neu-shadow-sm)]'
								: box.hasHistory || box.home
									? 'opacity-100 group-hover:bg-[var(--neu-hover)]'
									: 'opacity-40 group-hover:opacity-70'
						)}
					>
						{#if box.home}
							<HouseIcon class="size-3.5" />
						{:else}
							{box.emoji}
						{/if}
					</span>
				</button>
			{/each}
		</div>
	{/if}

	<!-- Token counter, pinned to the window's bottom-right corner. Shown only when a Token
	     Estimator is wired downstream of this chat's ConversationLog — the host pushes null otherwise
	     and the counter disappears. -->
	{#if tokenCount !== null && !showSetup}
		<div
			class="text-muted-foreground absolute right-3 bottom-2 text-[11px] tabular-nums select-none"
			title="Estimated tokens (Token Estimator on this chat's pipeline)"
		>
			{tokenCount.toLocaleString()} tokens
		</div>
	{/if}
</main>

<!-- The image editor sits OUTSIDE <main>, above everything, and only while an image is actually being
     marked up. Keyed on the image so re-opening the editor on a second picture starts a fresh mark-up
     history rather than inheriting the last one's. -->
{#if markUp}
	{#key markUp.base64}
		<ImageEditor
			base64={markUp.base64}
			mediaType={markUp.mediaType}
			label={markUp.label}
			onconfirm={confirmMarkUp}
			oncancel={cancelMarkUp}
		/>
	{/key}
{/if}
