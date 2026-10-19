using System.Text;

namespace Wpf.Portable.NativeHost.Linux.Accessibility;

internal static class AtSpiProtocol
{
    internal const string Accessible = "org.a11y.atspi.Accessible";
    internal const string Component = "org.a11y.atspi.Component";
    internal const string Action = "org.a11y.atspi.Action";
    internal const string Text = "org.a11y.atspi.Text";
    internal const string EditableText = "org.a11y.atspi.EditableText";
    internal const string Application = "org.a11y.atspi.Application";
    internal const string Properties = "org.freedesktop.DBus.Properties";
    internal const string Cache = "org.a11y.atspi.Cache";
    internal const string EventObject = "org.a11y.atspi.Event.Object";
    internal const string Root = "/org/a11y/atspi/accessible/root";
    internal const string Null = "/org/a11y/atspi/null";
    internal const string CachePath = "/org/a11y/atspi/cache";
    internal const string Registry = "org.a11y.atspi.Registry";

    internal static string Xml(IEnumerable<string> interfaces)
    {
        var xml = new StringBuilder("<node>");
        foreach (string name in interfaces.Append(Properties))
        {
            xml.Append("<interface name='").Append(name).Append("'>");
            void Method(string method, string[] input, params string[] output)
            {
                xml.Append("<method name='").Append(method).Append("'>");
                foreach (string type in input) xml.Append("<arg type='").Append(type).Append("' direction='in'/>");
                foreach (string type in output) xml.Append("<arg type='").Append(type).Append("' direction='out'/>");
                xml.Append("</method>");
            }
            void Property(string property, string type, string access = "read") => xml.Append("<property name='").Append(property).Append("' type='").Append(type).Append("' access='").Append(access).Append("'/>");
            switch (name)
            {
                case Properties:
                    Method("Get", ["s", "s"], "v"); Method("GetAll", ["s"], "a{sv}"); Method("Set", ["s", "s", "v"]); break;
                case Accessible:
                    Property("version", "u"); Property("Name", "s"); Property("Description", "s"); Property("Parent", "(so)");
                    Property("ChildCount", "i"); Property("Locale", "s"); Property("AccessibleId", "s");
                    Method("GetChildAtIndex", ["i"], "(so)"); Method("GetChildren", [], "a(so)"); Method("GetIndexInParent", [], "i");
                    Method("GetRelationSet", [], "a(ua(so))"); Method("GetRole", [], "u"); Method("GetRoleName", [], "s"); Method("GetLocalizedRoleName", [], "s");
                    Method("GetState", [], "au"); Method("GetAttributes", [], "a{ss}"); Method("GetApplication", [], "(so)"); Method("GetInterfaces", [], "as"); break;
                case Component:
                    Property("version", "u"); Method("Contains", ["i", "i", "u"], "b"); Method("GetAccessibleAtPoint", ["i", "i", "u"], "(so)");
                    Method("GetExtents", ["u"], "(iiii)"); Method("GetPosition", ["u"], "i", "i"); Method("GetSize", [], "i", "i");
                    Method("GetLayer", [], "u"); Method("GrabFocus", [], "b"); Method("GetAlpha", [], "d");
                    Method("SetPosition", ["i", "i", "u"], "b"); Method("SetSize", ["i", "i"], "b"); Method("SetExtents", ["i", "i", "i", "i", "u"], "b"); break;
                case Action:
                    Property("version", "u"); Property("NActions", "i"); Method("GetNActions", [], "i");
                    foreach (string member in new[] { "GetName", "GetLocalizedName", "GetDescription", "GetKeyBinding" }) Method(member, ["i"], "s");
                    Method("DoAction", ["i"], "b"); break;
                case Application:
                    Property("ToolkitName", "s"); Property("Version", "s"); Property("ToolkitVersion", "s"); Property("AtspiVersion", "s");
                    Property("InterfaceVersion", "u"); Property("Id", "i", "readwrite"); Method("GetLocale", ["u"], "s"); Method("GetApplicationBusAddress", [], "s"); break;
                case Text:
                    Property("version", "u"); Property("CharacterCount", "i"); Property("CaretOffset", "i");
                    Method("GetText", ["i", "i"], "s"); Method("GetCharacterAtOffset", ["i"], "i");
                    foreach (string member in new[] { "GetStringAtOffset", "GetTextAtOffset", "GetTextBeforeOffset", "GetTextAfterOffset" }) Method(member, ["i", "u"], "s", "i", "i");
                    Method("SetCaretOffset", ["i"], "b"); Method("GetNSelections", [], "i"); Method("GetSelection", ["i"], "i", "i");
                    Method("AddSelection", ["i", "i"], "b"); Method("SetSelection", ["i", "i", "i"], "b"); Method("RemoveSelection", ["i"], "b");
                    Method("GetCharacterExtents", ["i", "u"], "i", "i", "i", "i"); Method("GetRangeExtents", ["i", "i", "u"], "i", "i", "i", "i");
                    Method("GetOffsetAtPoint", ["i", "i", "u"], "i"); Method("ScrollSubstringTo", ["i", "i", "u"], "b");
                    Method("GetAttributeRun", ["i", "b"], "a{ss}", "i", "i"); Method("GetDefaultAttributeSet", [], "a{ss}");
                    Method("GetAttributes", ["i"], "a{ss}", "i", "i"); Method("GetDefaultAttributes", [], "a{ss}"); Method("GetAttributeValue", ["i", "s"], "s"); break;
                case EditableText:
                    Property("version", "u"); Method("SetTextContents", ["s"], "b"); Method("InsertText", ["i", "s", "i"], "b");
                    Method("DeleteText", ["i", "i"], "b"); Method("CopyText", ["i", "i"]); Method("CutText", ["i", "i"], "b"); Method("PasteText", ["i"], "b"); break;
                case Cache:
                    Property("version", "u"); Method("GetItems", [], "a((so)(so)(so)iiassusau)"); break;
                default: throw new ArgumentException("Unknown AT-SPI interface.", nameof(interfaces));
            }
            xml.Append("</interface>");
        }
        return xml.Append("</node>").ToString();
    }
}
