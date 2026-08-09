// Global ⌘K / Ctrl+K for the Workflow builder. The OrchestrationStudio component registers a .NET ref
// on first render and unregisters on dispose; there is at most one Workflow window (the window manager
// focuses the existing one), so a single document-level listener is sufficient. Capture phase + a guard
// against firing while the palette input itself is focused keeps it from hijacking normal typing.
window.agentosWorkflowKeys = {
    _handler: null,
    register: function (dotnetRef) {
        this.unregister();
        this._handler = function (e) {
            if ((e.ctrlKey || e.metaKey) && (e.key === 'k' || e.key === 'K')) {
                e.preventDefault();
                dotnetRef.invokeMethodAsync('OnQuickAddKey');
            }
        };
        document.addEventListener('keydown', this._handler, true);
    },
    unregister: function () {
        if (this._handler) {
            document.removeEventListener('keydown', this._handler, true);
            this._handler = null;
        }
    }
};
