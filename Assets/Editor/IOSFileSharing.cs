#if UNITY_IOS
using System.IO;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.iOS.Xcode;

/// <summary>
/// Makes the app's Documents folder visible in the iOS Files app and in Finder,
/// so saved surveys can be copied off the phone for analysis.
/// </summary>
public static class IOSFileSharing
{
    [PostProcessBuild]
    public static void OnPostprocessBuild(BuildTarget target, string pathToBuiltProject)
    {
        if (target != BuildTarget.iOS) return;

        string plistPath = Path.Combine(pathToBuiltProject, "Info.plist");
        var plist = new PlistDocument();
        plist.ReadFromFile(plistPath);

        plist.root.SetBoolean("UIFileSharingEnabled", true);
        plist.root.SetBoolean("LSSupportsOpeningDocumentsInPlace", true);

        plist.WriteToFile(plistPath);
    }
}
#endif
