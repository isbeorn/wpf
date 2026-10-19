# PresentationUI source wrapper

This project compiles the complete original PresentationUI assembly: all 93 C# inputs from its source manifest, all 13 XAML pages and both resource files. The original sources remain unchanged. The assembly retains version 10.0.0.0, the MicrosoftShared public key and the original ThemeInfo attribute. The existing portable markup compiler builds the original Generic theme and its PUIFlowDocumentScrollViewer component resource key.

PresentationFramework continues to use the official PresentationUI compile contract with Private=false. PresentationUI references source-built PresentationFramework in the opposite direction. This preserves the existing compile-time cycle break. A runtime producer must explicitly include this project, its genuine BAML and its same-family dependencies. Copying the compile contract into runtime output is not supported.

ReachFramework, System.Printing, WinForms, System.Resources.Extensions, System.Drawing.Common and System.DirectoryServices are compile-only contracts for original document, rights-management and installation code. They are not portable runtime implementations. These contracts come from the framework's existing WindowsDesktop 10.0.13 package. No Windows runtime PE is copied by these references. System.Windows.Extensions and System.IO.Packaging reuse the existing portable framework package versions.

The upstream ThemeGenerator.pl AfterBuild target exports SDK theme source files. It does not create runtime BAML and is not needed by this wrapper. All original runtime XAML pages are compiled directly.

The PresentationUIIntegration probe loads all nine compiled theme dictionaries, verifies the public resource-owner identity and exercises the original FlowDocumentScrollViewer default template, scroll host and initial empty-document child layout. It creates no window and requires no native backend. This does not qualify flow-document text formatting, XPS, printing, installation UI, WinForms dialogs or rights management. A non-null FlowDocument reaches the original PTS formatter, which remains a separate measured portability boundary.
