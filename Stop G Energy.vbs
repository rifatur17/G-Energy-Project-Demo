Option Explicit
Dim shell, files, root, command
Set shell = CreateObject("WScript.Shell")
Set files = CreateObject("Scripting.FileSystemObject")
root = files.GetParentFolderName(WScript.ScriptFullName)
command = "powershell.exe -NoProfile -ExecutionPolicy RemoteSigned -WindowStyle Hidden -File """ & root & "\scripts\Run-GEnergy.ps1"" -Action Stop"
shell.Run command, 0, False
