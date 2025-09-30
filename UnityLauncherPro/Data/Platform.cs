namespace UnityLauncherPro
{
    // TODO should display only valid platforms for projects Unity version
    public enum Platform
    {
        Unknown,
        Win32, Win64, OSX, Linux, Linux64, iOS, Android, Web, WebStreamed, WebGL, Xboxone, OS4, PSP2, WSAPlayer, Tizen, SamsungTV,
        Standalone, Win, OSXuniversal, LinuxUniversal, WindowsStoreApps, @Switch, Wiiu, N3DS, tVoS, PSM,
        // manually added
        StandaloneWindows, StandaloneWindows64
    }
}
