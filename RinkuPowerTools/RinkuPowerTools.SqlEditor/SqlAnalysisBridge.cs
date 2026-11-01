using System;
using System.Reflection;
using System.Windows.Forms;
using Microsoft.VisualStudio.Package;
using Microsoft.VisualStudio.TextManager.Interop;

namespace RinkuPowerTools.SqlEditor;

[System.Runtime.InteropServices.Guid("ed1a9c1c-d95c-4dc1-8db8-e5a28707a864")]
internal sealed class SqlAnalysisBridge : LanguageService {
    private static readonly FieldInfo SourceService = typeof(Source).GetField("service", BindingFlags.Instance | BindingFlags.NonPublic) ?? throw new MissingFieldException(typeof(Source).FullName, "service");
    private static readonly FieldInfo SourcesField = typeof(LanguageService).GetField("sources", BindingFlags.Instance | BindingFlags.NonPublic) ?? throw new MissingFieldException(typeof(LanguageService).FullName, "sources");
    private static readonly FieldInfo WindowsField = typeof(LanguageService).GetField("codeWindowManagers", BindingFlags.Instance | BindingFlags.NonPublic) ?? throw new MissingFieldException(typeof(LanguageService).FullName, "codeWindowManagers");
    private readonly LanguageService _native;

    public SqlAnalysisBridge(LanguageService native) {
        Microsoft.VisualStudio.Shell.ThreadHelper.ThrowIfNotOnUIThread();
        _native = native;
        SetSite(native.Site);
        SourcesField.SetValue(this, SourcesField.GetValue(native));
        WindowsField.SetValue(this, WindowsField.GetValue(native));
        OnActiveViewChanged(native.LastActiveTextView);
    }

    public void Attach(Source source) => SourceService.SetValue(source, this);
    public void Detach(Source source) {
        if (ReferenceEquals(source.LanguageService, this))
            SourceService.SetValue(source, _native);
    }

    public override string Name => _native.Name;
    public override LanguagePreferences GetLanguagePreferences() => _native.Preferences;
    public override string GetFormatFilterList() => _native.GetFormatFilterList();
    public override IScanner GetScanner(IVsTextLines buffer) => _native.GetScanner(buffer);
    public override ImageList GetImageList() => _native.GetImageList();
    public override bool IsSourceOpen(Source source) => _native.IsSourceOpen(source);
    public override void OnParseComplete(ParseRequest request) => _native.OnParseComplete(request);
    public override void OnCloseSource(Source source) {
        Detach(source);
        base.OnCloseSource(source);
        _native.OnCloseSource(source);
    }

    public override AuthoringScope ParseSource(ParseRequest request) {
        request.Text = request.Text?.Replace('?', ' ');
        return _native.ParseSource(request);
    }

    public override void Dispose() {
        Preferences = null;
        SourcesField.SetValue(this, null);
        WindowsField.SetValue(this, null);
        base.Dispose();
    }
}
