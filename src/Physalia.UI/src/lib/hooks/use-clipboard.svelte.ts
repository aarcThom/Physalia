// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

// Minimal clipboard helper used by the ai-elements copy-button. Writes text to
// the clipboard and exposes a transient status that resets after `delay` ms.
// (The shadcn-svelte registry references this hook but does not ship it.)

export type ClipboardStatus = 'idle' | 'success' | 'failure';

export class UseClipboard {
	#status = $state<ClipboardStatus>('idle');
	#timeout: ReturnType<typeof setTimeout> | undefined;
	delay: number;

	constructor(options: { delay?: number } = {}) {
		this.delay = options.delay ?? 500;
	}

	get status(): ClipboardStatus {
		return this.#status;
	}

	copy = async (text: string): Promise<ClipboardStatus> => {
		try {
			if (navigator?.clipboard?.writeText) {
				await navigator.clipboard.writeText(text);
				this.#status = 'success';
			} else {
				this.#status = 'failure';
			}
		} catch {
			this.#status = 'failure';
		}

		clearTimeout(this.#timeout);
		this.#timeout = setTimeout(() => {
			this.#status = 'idle';
		}, this.delay);

		return this.#status;
	};
}
