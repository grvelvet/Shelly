# expect: AddType/medium
Add-Type -MemberDefinition '[DllImport("user32.dll")] public static extern bool LockWorkStation();' -Name W -Namespace X
