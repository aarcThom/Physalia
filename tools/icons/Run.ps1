# Drives Split.ps1 over the seven sheets.
#
# The generated files are numbered in REVERSE of the prompt doc's sheet numbering (scratch
# sheet1.png is the doc's Sheet 7), and no sheet is on an even grid, so both the row layout and
# the name order below were read off the images by eye. sheet6 carries one extra blank chip with
# no mark inside it, which is dropped via the '-' placeholder.
param([switch]$ReportOnly)

$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$src = 'C:\Users\rober\repos\Physalia\Images\scratch'
$out = Join-Path $here 'out'

$sheets = @(
  @{ File='sheet1.png'; Rows=@(4,3,5); Names=@(
      'Router','WebSearch','ComponentSearch','RhinoCommonSearch',
      'ReadUrl','MemoryTool','RhinoGeometryTool',
      'AddImage','GeometrySnapshot','ViewSnapshot','ExportConversation','SignalTrace') },
  @{ File='sheet2.png'; Rows=@(4,4,4); Names=@(
      'TokenEstimator','TokenizationTechniques','TokenThreshold','SlidingWindow',
      'AnchoredWindow','TokenWindow','ContentPruner','Summarizer',
      'Serializer','Deserializer','Picker','ZoomGuid') },
  @{ File='sheet3.png'; Rows=@(4,3,2,3); Names=@(
      'Feedback','FeedbackCollector','MergeSignal','SignalLimiter',
      'BuildPlanTracker','ConstructSignal','DeconstructSignal',
      'MessageCompositor','MessageDecompositor',
      'ConversationCompositor','InstructionsCompositor','InstructionsDecompositor') },
  @{ File='sheet4.png'; Rows=@(5,5); Names=@(
      'SchemaValidator','GhDefinitionValidator','ComponentResolver','RequiredInputCheck','FidelityCheck',
      'RuntimeHealthCheck','DetectJson','GeometryObservation','GeometryReport','StallGuard') },
  @{ File='sheet5.png'; Rows=@(3,3,3); Names=@(
      'CanvasStateGrounder','PhysaliaGroupGrounder','ComponentCatalogGrounder',
      'ClusterGrounder','DocumentUnitsGrounder','ImageSources',
      'PythonGrounder','ScriptIO','ToolsInUse') },
  @{ File='sheet6.png'; Rows=@(4,4,5); Names=@(
      'AnthropicModel','GeminiModel','OpenAICompatibleModel','ClaudeCodeModel',
      'CodexModel','LlamaCppModelInfo','ModelInformation','-',
      'AnthropicTweaker','GeminiTweaker','OpenAICompatibleTweaker','ApiKeys','brain') },
  @{ File='sheet7.png'; Rows=@(3,3,3); Names=@(
      'HarnessComponent','SystemPrompt','ConversationLog',
      'LlmCall','HarnessNotes','ComponentTransmitter',
      'PyTransmitter','CsTransmitter','TextTransmitter') }
)

foreach ($s in $sheets) {
  Write-Output "=== $($s.File)  rows=$($s.Rows -join ',')  icons=$($s.Names.Count)"
  & (Join-Path $here 'Split.ps1') -Sheet (Join-Path $src $s.File) -Rows $s.Rows -Names $s.Names `
      -OutDir $out -ReportOnly:$ReportOnly
}
