using Microsoft.VisualStudio.Shell;

namespace RinkuPowerTools.SqlEditor;

internal sealed class SqlEditorRegistrationAttribute : RegistrationAttribute {
    public override void Register(RegistrationContext context) {
        using (var key = context.CreateKey("Editors\\{cc5d8df0-88f4-4bb2-9dbb-b48cee65c30a}\\Extensions"))
            key.SetValue("rnksql", 50);
        using (var key = context.CreateKey("Languages\\File Extensions\\.rnksql"))
            key.SetValue("", "{ed1a9c1c-d95c-4dc1-8db8-e5a28707a864}");
    }

    public override void Unregister(RegistrationContext context) {
        context.RemoveValue("Editors\\{cc5d8df0-88f4-4bb2-9dbb-b48cee65c30a}\\Extensions", "rnksql");
        context.RemoveKey("Languages\\File Extensions\\.rnksql");
    }
}
