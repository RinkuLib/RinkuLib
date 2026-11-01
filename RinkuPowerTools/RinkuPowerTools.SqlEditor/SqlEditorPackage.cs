using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Package;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.TextManager.Interop;

namespace RinkuPowerTools.SqlEditor;

[PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
[ProvideAutoLoad(UIContextGuids80.NoSolution, PackageAutoLoadFlags.BackgroundLoad)]
[ProvideAutoLoad(UIContextGuids80.SolutionExists, PackageAutoLoadFlags.BackgroundLoad)]
[SqlEditorRegistration]
[Guid("45c46f30-7749-44bb-bd63-481c8654839f")]
public sealed class SqlEditorPackage : AsyncPackage, IVsRunningDocTableEvents {
    private readonly Dictionary<uint, Source> _sources = new();
    private IVsRunningDocumentTable _documents;
    private LanguageService _native;
    private SqlAnalysisBridge _bridge;
    private uint _subscription;

    protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress) {
        await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
        try {
            if (await GetServiceAsync(typeof(SVsRunningDocumentTable)) is not IVsRunningDocumentTable documentsService)
                throw new InvalidOperationException("Visual Studio's running document table is unavailable.");
            _documents = documentsService;
            ErrorHandler.ThrowOnFailure(_documents.AdviseRunningDocTableEvents(this, out _subscription));
            ErrorHandler.ThrowOnFailure(_documents.GetRunningDocumentsEnum(out var documents));
            var cookie = new uint[1];
            while (documents.Next(1, cookie, out uint fetched) == VSConstants.S_OK && fetched != 0)
                Attach(cookie[0]);
        }
        catch (Exception error) {
            ActivityLog.LogError(nameof(SqlEditorPackage), error.ToString());
            throw;
        }
    }

    private void Attach(uint cookie, IVsWindowFrame frame = null) {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (_sources.ContainsKey(cookie))
            return;
        IntPtr data = IntPtr.Zero;
        try {
            if (ErrorHandler.Failed(_documents.GetDocumentInfo(cookie, out _, out _, out _, out string path, out _, out _, out data)))
                return;
            if (path == null || !path.EndsWith(".rnksql", StringComparison.OrdinalIgnoreCase) || data == IntPtr.Zero)
                return;
            var document = Marshal.GetObjectForIUnknown(data);
            var buffer = document as IVsTextLines;
            if (buffer == null && document is IVsTextBufferProvider provider)
                ErrorHandler.ThrowOnFailure(provider.GetTextBuffer(out buffer));
            if (buffer == null && frame != null)
                ErrorHandler.ThrowOnFailure(VsShellUtilities.GetTextView(frame).GetBuffer(out buffer));
            if (buffer == null)
                return;
            _native ??= (LanguageService)ServiceProvider.GlobalProvider.GetService(new Guid("ed1a9c1c-d95c-4dc1-8db8-e5a28707a864"));
            if (_native == null)
                return;
            var source = _native.GetOrCreateSource(buffer);
            _bridge ??= new SqlAnalysisBridge(_native);
            _bridge.Attach(source);
            source.IsDirty = true;
            _bridge.OnActiveViewChanged(frame != null ? VsShellUtilities.GetTextView(frame) : _native.GetPrimaryViewForSource(source));
            _sources.Add(cookie, source);
            source.BeginParse();
            ActivityLog.LogInformation(nameof(SqlEditorPackage), "Attached SQL analysis bridge: " + path);
        }
        catch (Exception error) {
            ActivityLog.LogError(nameof(SqlEditorPackage), error.ToString());
        }
        finally {
            if (data != IntPtr.Zero)
                Marshal.Release(data);
        }
    }

    public int OnBeforeDocumentWindowShow(uint cookie, int firstShow, IVsWindowFrame frame) {
        ThreadHelper.ThrowIfNotOnUIThread();
        Attach(cookie, frame);
        if (_sources.ContainsKey(cookie))
            _bridge.OnActiveViewChanged(VsShellUtilities.GetTextView(frame));
        return VSConstants.S_OK;
    }

    public int OnBeforeLastDocumentUnlock(uint cookie, uint flags, uint readLocks, uint editLocks) {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (_sources.TryGetValue(cookie, out var source)) {
            _bridge.Detach(source);
            _sources.Remove(cookie);
        }
        return VSConstants.S_OK;
    }

    public int OnAfterFirstDocumentLock(uint cookie, uint flags, uint readLocks, uint editLocks) => VSConstants.S_OK;
    public int OnAfterSave(uint cookie) => VSConstants.S_OK;
    public int OnAfterAttributeChange(uint cookie, uint attributes) => VSConstants.S_OK;
    public int OnAfterDocumentWindowHide(uint cookie, IVsWindowFrame frame) => VSConstants.S_OK;

    protected override void Dispose(bool disposing) {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (disposing) {
            if (_subscription != 0)
                _documents.UnadviseRunningDocTableEvents(_subscription);
            foreach (var source in _sources.Values)
                _bridge.Detach(source);
            _sources.Clear();
            _bridge?.Dispose();
        }
        base.Dispose(disposing);
    }
}
