using System;
using System.Collections.Generic;
using Biubiu;

static class CursorTitleTests
{
    static int failures;

    static int Main()
    {
        List<string> names = new List<string>();
        names.Add("Biubiu");
        names.Add("cemoi");
        names.Add("cemoi - \u2160");
        names.Add("cemoi - \u2161");
        names.Add("cemoi-ai\u5b8c\u5584");

        Expect("backend.mbc - Biubiu - Cursor", names, "Biubiu");
        Expect(".gitignore - cemoi-ai\u5b8c\u5584 - Cursor", names, "cemoi-ai\u5b8c\u5584");
        Expect("git-error-1792760950715 - cemoi - Cursor", names, "cemoi");
        Expect("treeForm.ftl (Working Tree) (treeForm.ftl) - cemoi - \u2160 - Cursor", names, "cemoi - \u2160");
        Expect(".gitignore - cemoi - \u2161 - Cursor", names, "cemoi - \u2161");
        Expect("\u25CF backend.mbc - Biubiu - Cursor", names, "Biubiu");
        Expect("Cursor Agents", names, null);
        Expect("Notepad", names, null);

        List<string> none = new List<string>();
        Expect("notes.txt - NewProj - Cursor", none, "NewProj");
        Expect("NewProj - Cursor", none, "NewProj");
        Expect("cemoi - \u2161 - Cursor", none, "\u2161");

        ExpectPath(
            "file:///d%3A/develop/JetBrains/IntelliJ%20IDEA/Data/branch/cemoi%20-%20%E2%85%A1",
            "cemoi - \u2161");
        ExpectPath(
            "file:///d%3A/develop/JetBrains/IntelliJ%20IDEA/Data/home/Biubiu",
            "Biubiu");

        failures += HookTests.Run();
        failures += CardTests.Run();

        if (failures == 0)
        {
            Console.WriteLine("ok");
            return 0;
        }

        Console.Error.WriteLine("failed " + failures);
        return 1;
    }

    static void Expect(string title, IList<string> names, string project)
    {
        string actual = CursorTitle.ResolveProject(title, names);
        if (actual == project)
            return;
        failures++;
        Console.Error.WriteLine("title: " + title);
        Console.Error.WriteLine("expected: " + (project ?? "<null>"));
        Console.Error.WriteLine("actual: " + (actual ?? "<null>"));
    }

    static void ExpectPath(string uri, string folderName)
    {
        string path = WorkspaceCatalog.FileUriToPath(uri);
        string actual = WorkspaceCatalog.FolderName(path);
        if (actual == folderName)
            return;
        failures++;
        Console.Error.WriteLine("uri: " + uri);
        Console.Error.WriteLine("expected folder: " + folderName);
        Console.Error.WriteLine("actual folder: " + (actual ?? "<null>"));
    }
}
