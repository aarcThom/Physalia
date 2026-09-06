// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

// The contract between the Svelte chat UI and its Eto WebView host
// (Physalia.GH/Panels/ChatWindow.cs).
//
//   C# -> JS : the host calls window.physalia.{setHistory,setStream,setState}
//   JS -> C# : the app stashes the outgoing message as JSON on window
//              (__physaliaPending) and navigates to `phbridge://submit`.
//              The host cancels that navigation, then pulls the JSON back
//              with __physaliaTake(). A custom-URI navigation + a pull-back
//              read is used (instead of putting data in the URL) because an
//              image-bearing message is far larger than a URL can carry.

/** A committed image already in the conversation, base64-encoded for display. */
export interface UiImage {
	base64: string;
	mediaType: string;
}

/** A tool call surfaced from ToolCallContent / ToolResultContent in the conversation. */
export interface UiTool {
	id: string;
	name: string;
	/** Maps to the ai-elements Tool component state. */
	state: 'input-streaming' | 'input-available' | 'output-available' | 'output-error';
	/** Parsed tool input (object) when the JSON is valid, else the raw string. */
	input?: unknown;
	/** Tool result text (present once the tool has run). */
	output?: string;
	/** Error text when the tool failed. */
	errorText?: string;
}

/** The component a turn was produced by, as the host resolved it (nickname + icon). */
export interface UiSource {
	/** The node's current nickname on the canvas — its recorded name if it has since been deleted. */
	name: string;
	/** Its Grasshopper icon as a data: URI, or undefined when the node is gone / had no readable icon. */
	icon?: string;
}

/** One committed conversation turn, as pushed by the host. */
export interface UiMessage {
	id: string;
	role: 'user' | 'assistant';
	/** Raw turn text; assistant text may contain <think>...</think> reasoning. */
	text: string;
	images?: UiImage[];
	tools?: UiTool[];
	/** True when this user-role turn is auto-generated feedback (validation errors, fix-and-resubmit
	 *  messages) rather than text the human typed — the UI styles it apart. */
	feedback?: boolean;
	/** The pipeline component(s) that produced this turn — what a feedback turn's header names and
	 *  badges. Several when an aggregated (Merge Signal / Feedback Collector) turn joined branches;
	 *  absent for a human-typed prompt. */
	sources?: UiSource[];
}

/** Connection / busy state of the wired pipeline. */
export interface UiState {
	connected: boolean;
	busy: boolean;
	/** No LLM provider is configured yet — show the first-run setup screen. */
	needsSetup: boolean;
	/** The window is on Home — the entry screen. Distinguishes it from a Chat that merely has no
	 *  Conversation Log wired yet: both show the connect surface, but only Home offers the placement
	 *  options; an empty harness gets the logo alone. */
	home: boolean;
	status: string;
	/** Setup-screen ids of every provider already configured (matches providers.ts ids). */
	configuredProviders: string[];
	/** Per-provider state: what is available, from where, and whether the user connected it.
	 *  Availability alone is NOT configuration — see ProviderStatus. */
	providerStatuses: ProviderStatus[];
	/** True when a component-catalog grounding is wired into the Conversation Log (greys the grounding icon when false). */
	groundingWired: boolean;
	/** The available component tabs and their panels, for the grounding selector. */
	groundingTree: GroundingCategory[];
	/** The current grounding selection (included tabs/panels), or null = include everything (default). */
	groundingSelection: GroundingCategory[] | null;
	/** True when the grounding panel's "expose component signatures" toggle is on (typed signatures folded into the prompt instead of bare names). */
	exposeSignatures: boolean;
	/** The grounded components grouped by tab, for the "/c/<tab>/<component>" staged autocomplete. */
	availableComponents: ComponentTabInfo[];
	/** True when a cluster grounding is wired into the Conversation Log (greys the Clusters kind when false). */
	clustersWired: boolean;
	/** The clusters available in Files/CLUSTERS (name, description, I/O), for the cluster selector and the "/c/" autocomplete. */
	availableClusters: ClusterInfo[];
	/** The current cluster selection (included cluster names), or null = include everything (default). */
	clusterSelection: string[] | null;
	/** True when a Tools Present grounding is wired into the Conversation Log. */
	toolsWired: boolean;
	/** The names of the tools currently on the canvas, for the Tools page and the "/t/" autocomplete. */
	availableTools: string[];
	/** The current tools selection (enabled tool names), or null = include everything (default). */
	toolsSelection: string[] | null;
	/** True when a Canvas State grounding is wired into the Conversation Log (shows the Referenced Rhino Geometry page). */
	referencedGeometryWired: boolean;
	/** The parameters on the canvas that reference live Rhino geometry, for the Referenced Rhino Geometry page. */
	availableReferencedGeometry: ReferencedGeometryInfo[];
	/** True when a Python Function grounding is wired into the Conversation Log. */
	pythonWired: boolean;
	/** The python functions available to the model, for the Python page. */
	pythonFunctions: PythonFunctionInfo[];
	/** True when a document-units grounding is wired into the Conversation Log (shows the Document Units pill). */
	unitsWired: boolean;
	/** The active Rhino document's current unit system (what the model gets unless overridden). */
	documentUnits: string;
	/** The current document-units override, or null = use the live document units (default). */
	unitsOverride: string | null;
	/** Unit-system choices for the Document Units dropdown (includes the current doc value + any override). */
	unitOptions: string[];
	/** True when a Geometry Snapshot human tool is wired into the Conversation Log (shows the Geometry Snapshot page). */
	snapshotWired: boolean;
	/** True when a transmitter has generated geometry on the canvas right now — with the tool wired,
	 *  the composer shows its geometry button. */
	snapshotGeometryPresent: boolean;
	/** True when pressing the geometry button sends the snapshot straight off as its own message
	 *  carrying snapshotDefaultMessage (the tool's "Send With Default Message" toggle, on by
	 *  default); false attaches the snapshot to the prompt box instead, for the user to caption —
	 *  the message is unused then, so its editor is hidden. */
	snapshotSendsMessage: boolean;
	/** The tool's default message sent alongside the snapshot image (the Geometry Observation wording). */
	snapshotDefaultMessage: string;
	/** The current snapshot-message override, or null = use the tool's default message (default). */
	snapshotMessage: string | null;
	/** True when a View Snapshot human tool is wired into the Conversation Log (shows the View Snapshot
	 *  page and its view button). There is no armed companion flag: a view capture needs nothing on the
	 *  canvas, so wired is armed. */
	viewSnapshotWired: boolean;
	/** True when pressing the view button sends the capture straight off as its own message carrying
	 *  viewSnapshotDefaultMessage (the tool's "Send With Default Message" toggle, on by default); false
	 *  attaches the capture to the prompt box instead, for the user to caption. */
	viewSnapshotSendsMessage: boolean;
	/** The tool's default message sent alongside the view capture. */
	viewSnapshotDefaultMessage: string;
	/** The current view-snapshot message override, or null = use the tool's default message (default). */
	viewSnapshotMessage: string | null;
	/** True when an Add Image human tool is wired into the Conversation Log — without it, image
	 *  intake (paste, drag-drop, file picker) is fully disabled in the composer. */
	imageToolWired: boolean;
	/** True when an Export Conversation human tool is wired into the Conversation Log — shows the
	 *  header's export button, which asks the host to write this conversation to a .txt transcript. */
	exportToolWired: boolean;
	/** True when a Signal Trace human tool is wired into the Conversation Log — shows the header's
	 *  trace button, which opens the session's signal-trace window. */
	signalTraceToolWired: boolean;
	/** True when an Image Mark Up human tool is wired into the Conversation Log. Unlike the other
	 *  marker tools this adds no button of its own: it puts the image editor in front of every image
	 *  the human sends — a snapshot capture detours through it instead of going straight out, and each
	 *  image in the prompt box grows an edit button on its thumbnail. */
	markUpToolWired: boolean;
	/** True when a Token Count human tool is wired (shows its row in the Human Tools section). The
	 *  number itself arrives separately, via setTokenCount. */
	tokenCountToolWired: boolean;
	/** True when a Read PDF human tool is wired into the Conversation Log — without it, PDF intake
	 *  (the PDF button and drag-drop) is disabled and a dropped PDF is refused. Separate from
	 *  imageToolWired: a PDF is not an image, never travels as one, and never reaches the editor. */
	pdfToolWired: boolean;
	/** PDFs attached but not yet announced in a turn — what the composer draws as chips. Summaries
	 *  only; a PDF's bytes never cross the bridge from the host, because a drawing set can be
	 *  hundreds of megabytes and the page has no use for the file itself. */
	pendingPdfs: UiPdf[];
}

/** One attached PDF, as the composer draws it. */
export interface UiPdf {
	/** The short handle the model addresses this document by. */
	alias: string;
	/** The original file name, shown on the chip. */
	name: string;
	/** Page count, shown on the chip so the human can see the set is what they meant to attach. */
	pages: number;
}

/** Which snapshot tool a capture came from. Sent to the page with a send-mode capture bound for the
 *  image editor, and handed straight back on the submit payload so the host knows whose message the
 *  marked-up image rides — the page never carries that text itself. */
export type SnapshotKind = 'geometry-snapshot' | 'view-snapshot';

/** One tab (category) and its panels (sub-categories) in the grounding selector. */
export interface GroundingCategory {
	category: string;
	subCategories: string[];
}

/** One tab and its component names, for the "/c/<tab>/<component>" staged autocomplete. */
export interface ComponentTabInfo {
	tab: string;
	components: string[];
}

/** One available Grasshopper cluster, for the cluster selector and the "/c/" prompt autocomplete. */
export interface ClusterInfo {
	name: string;
	description: string;
	inputs: string[];
	outputs: string[];
}

/** One parameter on the canvas referencing live Rhino geometry, for the Referenced Rhino Geometry page. */
export interface ReferencedGeometryInfo {
	name: string;
	type: string;
}

/** One python function available to the model, for the Python Function grounding page. */
export interface PythonFunctionInfo {
	signature: string;
	docstring: string;
}

/** Grounding selection sent back to the host. all=true clears to include-everything (null). */
export interface GroundingSelectionPayload {
	all: boolean;
	/** Included [category, subCategory] leaf pairs. Ignored when all is true. */
	leaves: [string, string][];
}

/** Cluster selection sent back to the host. all=true clears to include-everything (null). */
export interface ClusterSelectionPayload {
	all: boolean;
	/** Included cluster names. Ignored when all is true. */
	names: string[];
}

/** Tools selection sent back to the host. all=true clears to include-every-present-tool (null). */
export interface ToolsSelectionPayload {
	all: boolean;
	/** Enabled tool names. Ignored when all is true. */
	names: string[];
}

/** Document-units override sent back to the host. reset=true clears to the live document units. */
export interface UnitsOverridePayload {
	reset: boolean;
	/** The override unit text. Ignored when reset is true. */
	units: string;
}

/** Snapshot message override sent back to the host (both snapshot tools use this shape, each under its
 *  own verb). reset=true clears to the tool's default. */
export interface SnapshotMessagePayload {
	reset: boolean;
	/** The override message text sent alongside the snapshot image. Ignored when reset is true. */
	message: string;
}

/**
 * One configured MCP server, for the "Configure MCP connections" page.
 *
 * `transport` says which half of the record is meaningful: a local server is a subprocess
 * (command/args/cwd/env), a remote one is a URL the Physalia bridge relays to (url/headers/scope).
 * That split is the standard `mcpServers` shape, not something Physalia invented.
 *
 * Values arrive EXACTLY as written, so a "${VAR}" reference is still a reference here. The page must
 * hand them back the same way: showing a resolved token and saving it would write the secret into
 * the file the reference existed to keep it out of.
 */
export interface UiMcpServer {
	name: string;
	transport: 'local' | 'remote';
	command: string;
	args: string[];
	cwd: string;
	/** Environment pairs as [name, value], in file order. */
	env: [string, string][];
	url: string;
	/** HTTP header pairs as [name, value], in file order. Remote servers only. */
	headers: [string, string][];
	scope: string;
	/** False for an entry carrying neither a command nor a URL — it is listed, but cannot connect. */
	runnable: boolean;
}

/** The MCP config as the page sees it, pushed by the host whenever the file changes. */
export interface McpConfig {
	servers: UiMcpServer[];
}

/** Outcome of an MCP save/delete, pushed back by the host after it writes the file. */
export interface McpResult {
	ok: boolean;
	message: string;
}

/**
 * One HTTP API the user has configured for the API Call node.
 *
 * The KEY is deliberately absent. Everything else here describes where the API is and how it is
 * addressed, which the page must show to be editable at all; the key is a secret held in the
 * encrypted credential store, and pushing it into the page so a form could redisplay it would put
 * it in the UI's memory for no gain. `hasKey` is what the page needs instead — enough to say "a key
 * is set" and to offer forgetting it. A blank key on save therefore means "leave it as it is", not
 * "clear it".
 *
 * What the API CONTAINS — the datasets, the field names — is not here either. That lives on the API
 * Call node, because it has to travel inside a preset and this file cannot.
 */
export interface UiApiEndpoint {
	name: string;
	baseUrl: string;
	auth: 'none' | 'bearerHeader' | 'customHeader' | 'queryParameter';
	/** Header name, or query parameter name, depending on `auth`. */
	authName: string;
	/** Text placed before the key in a custom header's value, e.g. "Apikey ". */
	authPrefix: string;
	/** An environment variable consulted for the key before the credential store. */
	envVar: string;
	/** Whether a key is available — from the environment variable or from the store. */
	hasKey: boolean;
	/** Where that key comes from: the variable's name, "stored", or '' when there is none. */
	keySource: string;
}

/** The configured APIs as the page sees them, pushed by the host when the store changes. */
export interface ApiConfig {
	endpoints: UiApiEndpoint[];
}

/** Outcome of an API save/delete/test, pushed back by the host. */
export interface ApiResult {
	ok: boolean;
	message: string;
}

/** One API entry sent back to the host to be written. Mirrors UiApiEndpoint, plus the key. */
export interface ApiEndpointPayload {
	name: string;
	baseUrl: string;
	auth: 'none' | 'bearerHeader' | 'customHeader' | 'queryParameter';
	authName: string;
	authPrefix: string;
	envVar: string;
	/** The key to store. Blank leaves any existing key untouched — see UiApiEndpoint. */
	key: string;
	/** The entry's previous name when a rename is being saved (else ''), so the host edits in place. */
	replacing: string;
}

/** One server entry sent back to the host to be written. Mirrors UiMcpServer, plus the rename hook. */
export interface McpServerPayload {
	name: string;
	transport: 'local' | 'remote';
	command: string;
	args: string[];
	cwd: string;
	env: [string, string][];
	url: string;
	headers: [string, string][];
	scope: string;
	/** The entry's previous name when a rename is being saved (else ''), so the host edits it in place. */
	replacing: string;
	/** True to connect straight after saving — which is what runs a remote server's OAuth sign-in. */
	signIn: boolean;
}

/**
 * One provider's state, pushed by the host on its probe tick.
 *
 * `source` says where a credential was found, or how availability was established:
 *   'none'        - nothing configures this provider yet
 *   'environment' - a key is in an environment variable (`detail` names it)
 *   'stored'      - a key/endpoint the user entered, in the encrypted store
 *   'detected'    - a CLI on PATH, or a local server answering
 *
 * `activated` is the separate question of whether the user OPTED IN. A key someone exported for
 * another tool, or a CLI installed for another purpose, is available without being chosen — so the
 * page offers it with one button rather than adopting it silently.
 */
export interface ProviderStatus {
	id: string;
	activated: boolean;
	source: 'none' | 'environment' | 'stored' | 'detected';
	detail?: string | null;
}

/** Outcome of a save-API-key request, pushed back by the host after it writes the config. */
export interface SetupResult {
	/** Provider id the result is for (matches a providers.ts id). */
	provider: string;
	/** True when the key was saved successfully. */
	ok: boolean;
	/** Human-readable message to show on the setup page. */
	message: string;
}

/**
 * One entry in the switcher row: a Chat component on the canvas, or the Home entry that leads it.
 * Home is not a Chat — it is the placement / provider-setup screen, always present and always first.
 */
export interface UiChat {
	/** The component's InstanceGuid — sent back with `ordinal` when its circle is clicked. 'home' for Home. */
	id: string;
	/** Unique render key for the row: `<guid>#<ordinal>`, or 'home'. NOT interchangeable with `id` —
	 *  two Chats can share an InstanceGuid (the same preset placed twice copies it out of the archive),
	 *  and a duplicate key collapses two circles into one. Always key the each block on this. */
	key: string;
	/** Position in the host's chat list, sent back on click so a shared guid can still be resolved.
	 *  -1 for Home. */
	ordinal: number;
	/** True for the Chat the window is currently viewing (its circle reads as selected). */
	active: boolean;
	/** True when this Chat's wired Conversation Log already holds a conversation (its circle is filled). */
	hasHistory: boolean;
	/** The sea/ocean emoji that identifies this Chat — shown as its switcher dot (and canvas icon). */
	emoji: string;
	/** InstanceGuid of the harness holding this Chat, or '' when it has none (loose on the canvas in a
	 *  pre-harness file). The row is ordered by harness, so a divider goes wherever this differs from
	 *  the previous entry's — never within a harness. Home carries the sentinel 'home', which matches
	 *  no real key, so it is always divided from the Chats that follow it. */
	harness: string;
	/** True for the single Home entry, drawn as a house icon rather than an emoji. */
	home: boolean;
}

/** A preset harness (.gh under Files/PRESETS) offered on the Add-preset page. */
export interface UiPreset {
	/** Library-relative path, e.g. "Physalia/claude_code_incremental.gh". The wire value when loading:
	 *  handed back verbatim and matched against the library host-side, never composed into a path. */
	file: string;
	/** Which library folder it came from — "Physalia", "User" or "Community". Groups the gallery. */
	folder: string;
	/** Display label: the file name without folder or .gh extension. */
	name: string;
	/** The text of the Harness Notes panel inside the preset, read out of its archive by the host —
	 *  the only description a .gh can carry. Null when the preset has no notes. */
	description?: string | null;
}

/** Functions the host invokes on the page (set by the app on mount). */
export interface PhysaliaHost {
	setHistory(messages: UiMessage[]): void;
	setStream(text: string | null): void;
	setState(state: UiState): void;
	setSetupResult(result: SetupResult | null): void;
	/** Token count from the Token Estimator a wired Token Count human tool is linked to, or null to
	 *  hide the counter (no such tool wired, none linked to an estimator, or no count produced yet). */
	setTokenCount(count: number | null): void;
	/** Bundled preset harnesses (from Files/PRESETS) for the Add-preset page. */
	setPresets(presets: UiPreset[]): void;
	/** The configured MCP servers, for the Configure-MCP page. Pushed when the store changes. */
	setMcpServers(config: McpConfig): void;
	/** Outcome of the last MCP save/delete, or null to clear it. */
	setMcpResult(result: McpResult | null): void;
	/** The configured HTTP APIs, for the Configure-APIs page. Pushed when the store changes. */
	setApiEndpoints(config: ApiConfig): void;
	/** Outcome of the last API save/delete/test, or null to clear it. */
	setApiResult(result: ApiResult | null): void;
	/** Every Chat on the canvas, for the bottom switcher row. */
	setChats(chats: UiChat[]): void;
	/** A viewport snapshot captured by the geometry button in attach mode (the Geometry Snapshot
	 *  tool's default message switched off): lands in the composer's attachment strip like a pasted
	 *  image and leaves on the user's own turn. */
	attachSnapshot(image: UiImage): void;
	/** A viewport capture from the view button in attach mode (the View Snapshot tool's default message
	 *  switched off). Its own lane, granted by its own tool: it lands in the composer's attachment strip
	 *  like a pasted image and leaves on the user's own turn. */
	attachViewSnapshot(image: UiImage): void;
	/** A capture from a snapshot tool in SEND mode with an Image Mark Up tool wired: it opens in the
	 *  image editor instead of being sent. Confirming sends it (marked up) under its `kind`; cancelling
	 *  abandons it — in send mode there is no plain attachment to fall back to, so there is nothing to
	 *  keep. */
	markUpSnapshot(image: UiImage, kind: SnapshotKind): void;
}

/** Strips a `data:<mime>;base64,` prefix, returning the raw base64 payload. The
 *  prompt box reads attached images with FileReader (which yields a data: URL);
 *  the host wants only the base64 bytes, so we strip the prefix before sending. */
export function stripDataUrl(url: string): string {
	const comma = url.indexOf(',');
	return comma >= 0 && url.startsWith('data:') ? url.slice(comma + 1) : url;
}

/** An image attached in the prompt box, ready to send to the host. */
export interface SubmitImage {
	/** base64 payload only (no data: prefix); the host decodes it to bytes. */
	base64: string;
	mediaType: string;
	filename: string;
}

/** The outgoing user message handed to the host. */
export interface SubmitMessage {
	text: string;
	images: SubmitImage[];
	/** Absent for a typed prompt. A snapshot kind marks a send-mode capture coming back from the image
	 *  editor: the host reads the message that rides it off the wired tool, so `text` stays empty.
	 *  'pdf-drop' marks dropped PDF files rather than a message at all — the host registers them and
	 *  returns, and they are announced with whatever prompt is sent next. */
	kind?: SnapshotKind | PdfDropKind;
}

/** Marks a payload carrying dropped PDF bytes rather than a message. Drag-and-drop is the one PDF
 *  intake path that cannot hand the host a real path — the DOM File API withholds it — so the bytes
 *  come over and the host spools them to a temp file. The PDF *button* opens a native picker
 *  host-side and moves nothing, which is what makes it the path for a large set. */
export type PdfDropKind = 'pdf-drop';

declare global {
	interface Window {
		physalia?: PhysaliaHost;
		__physaliaPending?: string;
		__physaliaTake?: () => string;
	}
}
