using System.Numerics;
using ImGuiNET;

namespace Meddle.UI;

//Based on https://gist.github.com/prime31/91d1582624eb2635395417393018016e
public class FilePicker
{
    private static readonly Dictionary<string, FilePicker> FilePickers = new();

    public readonly string RootFolder;
    public string CurrentFolder;
    public string? SelectedFile;
    public readonly string[]? AllowedExtensions;
    public readonly bool OnlyAllowFolders;
    
    public FilePicker(string rootFolder, string currentFolder, string? selectedFile, string[]? allowedExtensions, bool onlyAllowFolders)
    {
        RootFolder = rootFolder;
        CurrentFolder = currentFolder;
        SelectedFile = selectedFile;
        AllowedExtensions = allowedExtensions;
        OnlyAllowFolders = onlyAllowFolders;
    }

    public static FilePicker GetFolderPicker(string id, string startingPath) => GetFilePicker(id, startingPath, null, true);

    public static FilePicker GetFilePicker(string id, string startingPath, string[]? allowedExtensions = null, bool onlyAllowFolders = false)
    {
        if (File.Exists(startingPath))
        {
            startingPath = new FileInfo(startingPath).DirectoryName!;
        }
        else if (string.IsNullOrEmpty(startingPath) || !Directory.Exists(startingPath))
        {
            startingPath = Environment.CurrentDirectory;
            if (string.IsNullOrEmpty(startingPath))
                startingPath = AppContext.BaseDirectory;
        }

        if (!FilePickers.TryGetValue(id, out var fp))
        {
            fp = new FilePicker(startingPath, startingPath, null, allowedExtensions, onlyAllowFolders);
            FilePickers.Add(id, fp);
        }

        return fp;
    }

    public static void RemoveFilePicker(string id) => FilePickers.Remove(id);

    public bool DrawFileSaver()
    {
        // Draw browser, then file name input
        Draw(false);
        var fileName = Path.GetFileName(SelectedFile) ?? string.Empty;
        ImGui.InputText("File Name", ref fileName, 1024);
        SelectedFile = Path.Combine(CurrentFolder, fileName);
        if (ImGui.Button("Save"))
        {
            SelectedFile = Path.Combine(CurrentFolder, fileName);
            ImGui.CloseCurrentPopup();
            return true;
        }
        
        return false;
    }
    
    public bool Draw(bool drawOpenButton = true)
    {
        ImGui.Text("Current Folder: " + Path.GetFileName(RootFolder) + CurrentFolder.Replace(RootFolder, ""));
        bool result = false;

        if (ImGui.BeginChild(1, new Vector2(400, 400)))
        {
            var di = new DirectoryInfo(CurrentFolder);
            if (di.Exists)
            {
                if (di.Parent != null && CurrentFolder != RootFolder)
                {
                    if (ImGui.Selectable("../", false, ImGuiSelectableFlags.DontClosePopups))
                        CurrentFolder = di.Parent.FullName;
                }

                var fileSystemEntries = GetFileSystemEntries(di.FullName);
                foreach (var fse in fileSystemEntries)
                {
                    if (Directory.Exists(fse))
                    {
                        var name = Path.GetFileName(fse);
                        if (ImGui.Selectable(name + "/", false, ImGuiSelectableFlags.DontClosePopups))
                            CurrentFolder = fse;
                    }
                    else
                    {
                        var name = Path.GetFileName(fse);
                        var isSelected = SelectedFile == fse;
                        if (ImGui.Selectable(name, isSelected, ImGuiSelectableFlags.DontClosePopups))
                            SelectedFile = fse;
                    }
                }
            }
        }
        ImGui.EndChild();


        if (ImGui.Button("Cancel"))
        {
            result = false;
            ImGui.CloseCurrentPopup();
        }

        if (drawOpenButton && (OnlyAllowFolders || SelectedFile != null))
        {
            ImGui.SameLine();
            if (ImGui.Button("Open"))
            {
                result = true;
                if (OnlyAllowFolders)
                {
                    SelectedFile = CurrentFolder;
                }

                ImGui.CloseCurrentPopup();
            }
        }

        return result;
    }

    private List<string> GetFileSystemEntries(string fullName)
    {
        var files = new List<string>();
        var dirs = new List<string>();

        foreach (var fse in Directory.GetFileSystemEntries(fullName, ""))
        {
            if (Directory.Exists(fse))
            {
                dirs.Add(fse);
            }
            else if (!OnlyAllowFolders)
            {
                if (AllowedExtensions != null)
                {
                    var ext = Path.GetExtension(fse);
                    if (AllowedExtensions.Contains(ext))
                        files.Add(fse);
                }
                else
                {
                    files.Add(fse);
                }
            }
        }
		
        var ret = new List<string>(dirs);
        ret.AddRange(files);

        return ret;
    }
}
