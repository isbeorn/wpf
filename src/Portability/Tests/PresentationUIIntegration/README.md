# Original PresentationUI integration probe

Build and run PresentationUIIntegration with the portable framework's normal isolated build outputs. It uses the source-built PresentationUI project and the real public FlowDocumentScrollViewer. It loads the original compiled theme dictionaries and verifies the component-key style, automatic default template, PART_ContentHost ScrollViewer and initial FlowDocumentView child layout.

The probe creates no Application, window or native backend. It leaves Document in its original null state so it tests this assembly/resource boundary independently of the original PTS document formatter. Non-null document formatting, printing, XPS and the Windows document dialogs are not qualified by a pass.
