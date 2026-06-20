// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

#nullable enable

using System;
using System.Collections.Generic;
using System.Text.Json;
using Eto.Forms;
using Physalia.Core.ConvoInstruct;
using Physalia.GH.Components;

namespace Physalia.GH.Panels;

/// <summary>
/// Standalone Eto window hosting the web chat UI for a <see cref="Chatbox"/> component.
/// Sends go to the component as Prompt Signals (JS→C# via a custom-URI navigation
/// intercept). The committed conversation, the live streaming response, and the busy /
/// connect state are pulled from the wired Recorder by graph traversal (<see
/// cref="PromptPipelineView"/>) on a UI timer and pushed to the page (C#→JS via
/// ExecuteScript) — the same read path Prompter's canvas panel uses.
///
/// The page is still inline HTML; the Svelte build replaces it later (planning/chat-window.md).
/// </summary>
public class ChatWindow : Form
{
    private const string BridgeScheme = "phbridge";

    private static readonly JsonSerializerOptions JsonOpts =
        new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private const string Html = @"<!doctype html>
<html><head><meta charset='utf-8'><style>
  body{font-family:sans-serif;margin:0;display:flex;flex-direction:column;height:100vh;}
  #log{flex:1;overflow-y:auto;padding:8px;}
  .msg{margin:4px 0;white-space:pre-wrap;}
  .user{color:#70f;}
  .assistant{color:#024;}
  .streaming{color:#024;opacity:.7;}
  #status{font:11px sans-serif;color:#888;padding:2px 8px;min-height:14px;}
  #bar{display:flex;border-top:1px solid #ccc;}
  #box{flex:1;border:0;padding:8px;font-size:14px;resize:none;}
  #box:disabled{background:#f3f3f3;}
  #send{border:0;background:#70f;color:#fff;padding:0 16px;cursor:pointer;}
  #send:disabled{background:#bbb;cursor:default;}
</style></head>
<body>
  <div id='log'><div id='committed'></div></div>
  <div id='status'></div>
  <div id='bar'>
    <textarea id='box' rows='2' placeholder='Type a message, Enter to send'></textarea>
    <button id='send'>Send</button>
  </div>
<script>
  var committed=document.getElementById('committed');
  var logEl=document.getElementById('log');
  var streamEl=null;
  function scrollBottom(){logEl.scrollTop=logEl.scrollHeight;}
  window.ghHistory=function(items){
    committed.innerHTML='';
    items.forEach(function(m){
      var d=document.createElement('div');
      d.className='msg '+m.role;
      d.textContent=m.text;
      committed.appendChild(d);
    });
    scrollBottom();
  };
  window.ghStream=function(text){
    if(!text){ if(streamEl){streamEl.remove();streamEl=null;} return; }
    if(!streamEl){ streamEl=document.createElement('div'); streamEl.className='msg streaming'; logEl.appendChild(streamEl);}
    streamEl.textContent=text;
    scrollBottom();
  };
  window.ghState=function(enabled,status){
    document.getElementById('box').disabled=!enabled;
    document.getElementById('send').disabled=!enabled;
    document.getElementById('status').textContent=status||'';
  };
  function send(){
    var box=document.getElementById('box');
    var v=box.value.trim();
    if(!v) return;
    box.value='';
    window.location.href='phbridge://submit?text='+encodeURIComponent(v);
  }
  document.getElementById('send').addEventListener('click',send);
  document.getElementById('box').addEventListener('keydown',function(e){
    if(e.key==='Enter'&&!e.shiftKey){e.preventDefault();send();}
  });
</script>
</body></html>";

    private readonly Chatbox _component;
    private readonly WebView _webView;
    private readonly UITimer _timer;
    private bool _loaded;

    // last-pushed state, for change detection so we only ExecuteScript on a real change
    private Conversation? _lastConversation;
    private string? _lastStream;
    private bool? _lastEnabled;
    private string? _lastStatus;

    /// <summary>
    /// Initializes a new instance of the <see cref="ChatWindow"/> class.
    /// </summary>
    /// <param name="component">The Chatbox component this window drives.</param>
    public ChatWindow(Chatbox component)
    {
        _component = component ?? throw new ArgumentNullException(nameof(component));

        Title = "Physalia Chat";
        ClientSize = new Eto.Drawing.Size(420, 520);
        Resizable = true;
        Owner = Rhino.UI.RhinoEtoApp.MainWindow;

        _webView = new WebView();
        _webView.DocumentLoading += OnDocumentLoading;
        Content = _webView;

        // Pull pipeline state and push it to the page. GH never re-solves on a wire
        // connection, so polling is the simplest correct refresh (same cadence as
        // Prompter's busy animation). Ticks run on the UI thread.
        _timer = new UITimer { Interval = 0.1 };
        _timer.Elapsed += (_, _) => Tick();

        // Load once the native handle exists — loading in the ctor is dropped on some backends.
        Shown += (_, _) =>
        {
            if (_loaded)
            {
                return;
            }

            _loaded = true;
            _webView.LoadHtml(Html);
            _timer.Start();
        };

        Closed += (_, _) => _timer.Stop();
    }

    // JS→C# bridge: JS navigates to phbridge://submit?text=... ; we cancel the navigation
    // and handle it instead. Same intercept works on WebView2 and WKWebView.
    private void OnDocumentLoading(object? sender, WebViewLoadingEventArgs e)
    {
        if (e.Uri.Scheme != BridgeScheme)
        {
            return;
        }

        e.Cancel = true; // must be synchronous to actually cancel the navigation

        // Defer the work off the navigation callback: running a GH solve synchronously here
        // re-enters the WebView2 core mid-navigation and crashes Rhino (same hazard as
        // ManageImagesDialog's grid-edit deferral). The Tick loop renders the recorded turn.
        string text = ParseTextQuery(e.Uri);
        Application.Instance.AsyncInvoke(() => _component.SubmitFromWindow(text));
    }

    // Pulls current pipeline state and pushes only what changed to the page.
    private void Tick()
    {
        if (!_loaded)
        {
            return;
        }

        Recorder? recorder = PromptPipelineView.FindRecorder(_component, 0);
        Conversation? convo = recorder?.ActiveConversation;
        bool busy = recorder is not null && PromptPipelineView.IsPipelineBusy(recorder);

        bool enabled = recorder is not null && !busy;
        string status = recorder is null ? "Connect a Recorder to begin."
            : busy ? "Working…"
            : string.Empty;

        if (!ReferenceEquals(convo, _lastConversation))
        {
            _lastConversation = convo;
            Exec($"ghHistory({JsonSerializer.Serialize(BuildLines(convo), JsonOpts)});");
        }

        string? stream = busy ? PromptPipelineView.GetStreamingText(recorder!) : null;
        if (stream != _lastStream)
        {
            _lastStream = stream;
            Exec($"ghStream({JsonSerializer.Serialize(stream)});");
        }

        if (enabled != _lastEnabled || status != _lastStatus)
        {
            _lastEnabled = enabled;
            _lastStatus = status;
            Exec($"ghState({(enabled ? "true" : "false")}, {JsonSerializer.Serialize(status)});");
        }
    }

    private static List<Line> BuildLines(Conversation? convo)
    {
        var lines = new List<Line>();
        if (convo is null)
        {
            return lines;
        }

        foreach (ConversationMessage message in convo.Messages)
        {
            lines.Add(new Line(message.Role == Role.User ? "user" : "assistant", Flatten(message)));
        }

        return lines;
    }

    // flattens a message's content blocks into one display string (images shown as a tag)
    private static string Flatten(ConversationMessage message)
    {
        var parts = new List<string>(message.Content.Count);
        foreach (MessageContent block in message.Content)
        {
            parts.Add(block is TextContent text ? text.Text : "[image]");
        }

        return string.Join(Environment.NewLine, parts);
    }

    // C#→JS push; swallows the rare race where a tick fires before the page script is ready.
    private void Exec(string script)
    {
        try
        {
            _webView.ExecuteScript(script);
        }
        catch
        {
            // page not ready yet (or torn down) — next tick will resend the current state
        }
    }

    private static string ParseTextQuery(Uri uri)
    {
        const string key = "text=";
        string query = uri.Query; // "?text=..."
        int i = query.IndexOf(key, StringComparison.Ordinal);
        return i < 0 ? string.Empty : Uri.UnescapeDataString(query.Substring(i + key.Length));
    }

    private readonly record struct Line(string Role, string Text);
}
