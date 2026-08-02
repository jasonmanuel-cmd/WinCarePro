Option Explicit

Dim shell, files, root, pythonw, app
Set shell = CreateObject("Shell.Application")
Set files = CreateObject("Scripting.FileSystemObject")

root = files.GetParentFolderName(WScript.ScriptFullName)
pythonw = files.BuildPath(root, ".venv\Scripts\pythonw.exe")
app = files.BuildPath(root, "main.py")

If Not files.FileExists(pythonw) Then
    MsgBox "WinCarePro is not installed yet. Run build.ps1 once, then start WinCarePro again.", 16, "WinCarePro"
    WScript.Quit 1
End If

shell.ShellExecute pythonw, Chr(34) & app & Chr(34), root, "runas", 0
