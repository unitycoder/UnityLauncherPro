' creates GUID for installer product code
Set obj = CreateObject("Scriptlet.TypeLib")
WScript.Echo Mid(obj.GUID, 2, 36)
