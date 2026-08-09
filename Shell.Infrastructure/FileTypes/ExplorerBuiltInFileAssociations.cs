using Shell.Core.FileTypes;

namespace Shell.Infrastructure.FileTypes;

internal static class ExplorerBuiltInFileAssociations
{
    private const string TextDocumentIcon = @"%SystemRoot%\system32\imageres.dll,-102";
    private const string SetupInformationIcon = @"%SystemRoot%\System32\imageres.dll,-69";
    private const string SecurityCatalogIcon = @"cryptui.dll,-3418";
    private const string CertificateFileIcon = @"cryptui.dll,-3410";
    private const string ZipIcon = @"%SystemRoot%\System32\imageres.dll,-174";
    private const string DllIcon = @"%SystemRoot%\system32\imageres.dll,-67";
    private const string SystemFileIcon = @"%SystemRoot%\system32\imageres.dll,-79";
    private const string ExecutableIcon = @"%SystemRoot%\System32\imageres.dll,-15";
    private const string PowerShellScriptIcon = @"%SystemRoot%\System32\imageres.dll,-5372";
    private const string CommandScriptIcon = @"%SystemRoot%\System32\imageres.dll,-68";
    private const string VbsScriptIcon = @"%SystemRoot%\System32\WScript.exe,2";
    private const string JsScriptIcon = @"%SystemRoot%\System32\WScript.exe,3";
    private const string ScriptletIcon = @"%SystemRoot%\System32\scrobj.dll,0";
    private const string RegistryIcon = @"%SystemRoot%\regedit.exe,1";
    private const string XslIcon = @"%SystemRoot%\System32\msxml3.dll,1";
    private const string PresentationHostDocumentIcon = @"%SystemRoot%\System32\PresentationHost.exe,2";
    private const string ImageIcon = @"%SystemRoot%\System32\imageres.dll,-132";

    private const string NotepadRegistryOpenCommand = @"""%SystemRoot%\System32\notepad.exe"" ""%1""";


    private static readonly ExplorerOpenCommand NotepadCommand =
        new("notepad", "Notepad", "%SystemRoot%\\System32\\notepad.exe", "\"%1\"",
            new ExplorerFileIconIdentity(ExplorerFileIconIdentityKind.FilePath, "%SystemRoot%\\System32\\notepad.exe"));

    private static readonly ExplorerOpenCommand RegistryMergeCommand =
    new(
        "mergeRegistry",
        "Registry Editor",
        "%SystemRoot%\\regedit.exe",
        "\"%1\"",
        new ExplorerFileIconIdentity(
                ExplorerFileIconIdentityKind.FilePath,
                "%SystemRoot%\\regedit.exe"));

    private static readonly ExplorerOpenCommand PowerShellCommand =
        new("powershell", "PowerShell", "%SystemRoot%\\System32\\WindowsPowerShell\\v1.0\\powershell.exe", "-ExecutionPolicy Bypass -File \"%1\"");

    private static readonly ExplorerOpenCommand CommandPromptCommand =
        new("cmd", "Command Prompt", "%SystemRoot%\\System32\\cmd.exe", "/c \"\"%1\"\"");

    private static readonly ExplorerOpenCommand WindowsScriptHostCommand =
        new(
            "wscript",
            "Windows Script Host",
            "%SystemRoot%\\System32\\WScript.exe",
            "\"%1\" %*");

    private static readonly ExplorerOpenCommand SecurityCatalogCommand =
        new(
            "openCat",
            "Open",
            "%SystemRoot%\\System32\\rundll32.exe",
            "cryptext.dll,CryptExtOpenCAT \"%1\"");

    private static readonly ExplorerOpenCommand Pkcs7CertificateCommand =
        new(
            "openPkcs7",
            "Open",
            "%SystemRoot%\\System32\\rundll32.exe",
            "cryptext.dll,CryptExtOpenPKCS7 \"%1\"");

    private static readonly ExplorerOpenCommand CertificateStoreCommand =
        new(
            "openCertificateStore",
            "Open",
            "%SystemRoot%\\System32\\rundll32.exe",
            "cryptext.dll,CryptExtOpenSTR \"%1\"");

    private static readonly ExplorerOpenCommand PresentationHostCommand =
        new(
            "presentationHost",
            "PresentationHost",
            "%SystemRoot%\\System32\\PresentationHost.exe",
            "\"%1\" %*");

    private static readonly ExplorerOpenCommand ComExecuteCommand =
        new(
            "executeCom",
            "Run",
            "%1",
            "%*");

    private static readonly BuiltInFileTypeDefinition[] Definitions =
    [
        Edit(".txt", "WinPeShell.TextDocument", "Text Document", ExplorerKnownFileTypeIds.Text, "text", "text/plain", TextDocumentIcon),
        Edit(".log", "WinPeShell.TextDocument", "Text Document", ExplorerKnownFileTypeIds.Text, "text", null, TextDocumentIcon),
        Edit(".csv", "WinPeShell.CsvFile", "Text Document", ExplorerKnownFileTypeIds.Text, "text", null, TextDocumentIcon, registryFriendlyTypeName: "CSV File"),
        Edit(".config", "WinPeShell.TextDocument", "Text Document", ExplorerKnownFileTypeIds.Text, "text", null, TextDocumentIcon),

        Edit(".ini", "WinPeShell.ConfigurationSettings", "Configuration Settings", ExplorerKnownFileTypeIds.SetupInformation, "text", null, SetupInformationIcon),
        Edit(".inf", "WinPeShell.SetupInformation", "Setup Information", ExplorerKnownFileTypeIds.SetupInformation, "text", null, SetupInformationIcon),

        Open(".cat", "WinPeShell.SecurityCatalog", "Security Catalog", ExplorerKnownFileTypeIds.Catalog, BuiltInOpenCommandKind.SecurityCatalog, null, "application/vnd.ms-pki.seccat", SecurityCatalogIcon, @"%SystemRoot%\system32\rundll32.exe cryptext.dll,CryptExtOpenCAT ""%1"""),
        Open(".p7b", "WinPeShell.Pkcs7Certificates", "PKCS #7 Certificates", ExplorerKnownFileTypeIds.Pkcs7Certificate, BuiltInOpenCommandKind.Pkcs7Certificate, null, "application/x-pkcs7-certificates", CertificateFileIcon, @"%SystemRoot%\system32\rundll32.exe cryptext.dll,CryptExtOpenPKCS7 ""%1"""),
        Open(".sst", "WinPeShell.CertificateStore", "Microsoft Serialized Certificate Store", ExplorerKnownFileTypeIds.CertificateStore, BuiltInOpenCommandKind.CertificateStore, null, "application/vnd.ms-pki.certstore", CertificateFileIcon, @"%SystemRoot%\system32\rundll32.exe cryptext.dll,CryptExtOpenSTR ""%1"""),

        TypeOnly(".zip", "WinPeShell.Zip", "Compressed (zipped) Folder", ExplorerKnownFileTypeIds.Zip, "compressed", "application/x-zip-compressed", ZipIcon),
        TypeOnly(".gz", "WinPeShell.GzArchive", "Compressed Archive Folder", ExplorerKnownFileTypeIds.CompressedArchive, "compressed", "application/x-gzip", ZipIcon),

        TypeOnly(".dll", "WinPeShell.ApplicationExtension", "Application extension", ExplorerKnownFileTypeIds.Dll, null, "application/x-msdownload", DllIcon),
        TypeOnly(".rll", "WinPeShell.ApplicationExtension", "Application extension", ExplorerKnownFileTypeIds.Dll, null, null, DllIcon),
        TypeOnly(".pnf", "WinPeShell.PrecompiledSetupInformation", "Precompiled Setup Information", ExplorerKnownFileTypeIds.Dll, null, null, DllIcon),
        TypeOnly(".db", "WinPeShell.DataBaseFile", "Data Base File", ExplorerKnownFileTypeIds.Dll, null, null, DllIcon),

        TypeOnly(".drv", "WinPeShell.DeviceDriver", "Device driver", ExplorerKnownFileTypeIds.SystemFile, "system", null, SystemFileIcon),
        TypeOnly(".sys", "WinPeShell.SystemFile", "System file", ExplorerKnownFileTypeIds.SystemFile, "system", null, SystemFileIcon),
        TypeOnly(".cpl", "WinPeShell.ControlPanelItem", "Control panel item", ExplorerKnownFileTypeIds.SystemFile, null, null, SystemFileIcon),

        Open(".com", "WinPeShell.MsDosApplication", "MS-DOS Application", ExplorerKnownFileTypeIds.ComExecutable, BuiltInOpenCommandKind.ComExecute, null, null, ExecutableIcon, @"""%1"" %*"),
        Executable(".exe", "Application", ExplorerKnownFileTypeIds.Executable),

        Script(".ps1", "WinPeShell.PowerShellScript", "Windows PowerShell Script", ExplorerKnownFileTypeIds.PowerShellScript, BuiltInOpenCommandKind.PowerShell, null, null, PowerShellScriptIcon),
        Script(".bat", "WinPeShell.BatchFile", "Windows Batch File", ExplorerKnownFileTypeIds.CommandScript, BuiltInOpenCommandKind.CommandPrompt, null, null, CommandScriptIcon),
        Script(".cmd", "WinPeShell.CommandScript", "Windows Command Script", ExplorerKnownFileTypeIds.CommandScript, BuiltInOpenCommandKind.CommandPrompt, null, null, CommandScriptIcon),
        Script(".vbs", "WinPeShell.VbScript", "VBScript Script File", ExplorerKnownFileTypeIds.VbsScript, BuiltInOpenCommandKind.WindowsScriptHost, null, null, VbsScriptIcon),
        Script(".js", "WinPeShell.JavaScriptFile", "JavaScript File", ExplorerKnownFileTypeIds.JsScript, BuiltInOpenCommandKind.WindowsScriptHost, null, null, JsScriptIcon),
        TypeOnly(".wsc", "WinPeShell.ScriptComponent", "Windows Script Component", ExplorerKnownFileTypeIds.Scriptlet, null, "text/scriptlet", ScriptletIcon),

        RegistryFile(".reg", "WinPeShell.RegistrationEntries", "Registration Entries", ExplorerKnownFileTypeIds.Registry, null, null, RegistryIcon),

        Edit(".xsl", "WinPeShell.XslStylesheet", "XSL Stylesheet", ExplorerKnownFileTypeIds.Xsl, "text", "text/xml", XslIcon),
        Edit(".css", "WinPeShell.CssDocument", "Cascading Style Sheet Document", ExplorerKnownFileTypeIds.Css, "text", "text/css", SetupInformationIcon),
        Edit(".compositefont", "WinPeShell.CompositeFont", "Composite Font File", ExplorerKnownFileTypeIds.PresentationHostDocument, null, null, PresentationHostDocumentIcon),
        Open(".xaml", "WinPeShell.XamlDocument", "Windows Markup File", ExplorerKnownFileTypeIds.PresentationHostDocument, BuiltInOpenCommandKind.PresentationHost, null, "application/xaml+xml", PresentationHostDocumentIcon, @"""%SystemRoot%\System32\PresentationHost.exe"" ""%1"" %*"),

        TypeOnly(".gif", "WinPeShell.GifImage", "GIF Image", ExplorerKnownFileTypeIds.Image, "image", "image/gif", ImageIcon),
        TypeOnly(".jpg", "WinPeShell.JpegImage", "JPEG Image", ExplorerKnownFileTypeIds.Image, "image", "image/jpeg", ImageIcon),
        TypeOnly(".jpeg", "WinPeShell.JpegImage", "JPEG Image", ExplorerKnownFileTypeIds.Image, "image", "image/jpeg", ImageIcon),
        TypeOnly(".png", "WinPeShell.PngImage", "PNG Image", ExplorerKnownFileTypeIds.Image, "image", "image/png", ImageIcon),
        TypeOnly(".bmp", "WinPeShell.BitmapImage", "Image File", ExplorerKnownFileTypeIds.Image, "image", "image/bmp", ImageIcon, registryFriendlyTypeName: "Bitmap Image"),
        TypeOnly(".ico", "WinPeShell.IconFile", "Icon File", ExplorerKnownFileTypeIds.Image, "image", "image/x-icon", ImageIcon),
    ];

    public static IReadOnlyDictionary<string, ExplorerFileAssociation> Create()
    {
        Dictionary<string, ExplorerFileAssociation> associations = new(StringComparer.OrdinalIgnoreCase);

        foreach (BuiltInFileTypeDefinition definition in Definitions)
            AddDefinition(associations, definition);

        return associations;
    }

    internal static IReadOnlyList<BuiltInFileTypeDefinition> GetDefinitions()
    {
        return Definitions;
    }


    private static void AddDefinition(
        Dictionary<string, ExplorerFileAssociation> associations,
        BuiltInFileTypeDefinition definition)
    {
        switch (definition.AssociationKind)
        {
            case BuiltInFileAssociationKind.TypeOnly:
                AddKnownNoDefault(associations, definition.Extension, definition.DisplayName, definition.KnownType);
                break;

            case BuiltInFileAssociationKind.EditInNotepad:
                AddKnownEditInNotepad(associations, definition.Extension, definition.DisplayName, definition.KnownType);
                break;

            case BuiltInFileAssociationKind.OpenCommand:
                AddKnownWithDefault(
                associations,
                definition.Extension,
                definition.DisplayName,
                definition.KnownType,
                GetRequiredOpenCommand(definition.CommandKind));
                break;

            case BuiltInFileAssociationKind.Script:
                AddScript(
                associations,
                definition.Extension,
                definition.DisplayName,
                definition.KnownType,
                GetOpenCommand(definition.CommandKind));
                break;

            case BuiltInFileAssociationKind.RegistryFile:
                Add(associations, new ExplorerFileAssociation(
                definition.Extension,
                definition.DisplayName,
                new ExplorerFileIconIdentity(ExplorerFileIconIdentityKind.KnownType, definition.KnownType),
                NotepadCommand,
                [NotepadCommand],
                [new ExplorerFileVerb(RegistryMergeCommand.Id, "Merge", RegistryMergeCommand)],
                IsUserDefined: false));
                break;

            case BuiltInFileAssociationKind.Executable:
                Add(associations, new ExplorerFileAssociation(
                definition.Extension,
                definition.DisplayName,
                new ExplorerFileIconIdentity(ExplorerFileIconIdentityKind.KnownType, definition.KnownType),
                new ExplorerOpenCommand("execute", "Run", "%1", string.Empty), [], [], IsUserDefined: false));
                break;

        }
    }

    private static BuiltInFileTypeDefinition TypeOnly(
        string extension,
        string progId,
        string displayName,
        string knownType,
        string? perceivedType,
        string? contentType,
        string registryDefaultIcon,
        string? registryFriendlyTypeName = null)
    {
        return new BuiltInFileTypeDefinition(
            extension,
            displayName,
            knownType,
            BuiltInFileAssociationKind.TypeOnly,
            RegistryProgId: progId,
            RegistryFriendlyTypeName: registryFriendlyTypeName,
            PerceivedType: perceivedType,
            ContentType: contentType,
            RegistryDefaultIcon: registryDefaultIcon);
    }

    private static BuiltInFileTypeDefinition Edit(
        string extension,
        string progId,
        string displayName,
        string knownType,
        string? perceivedType,
        string? contentType,
        string registryDefaultIcon,
        string? registryFriendlyTypeName = null)
    {
        return new BuiltInFileTypeDefinition(
            extension,
            displayName,
            knownType,
            BuiltInFileAssociationKind.EditInNotepad,
            RegistryProgId: progId,
            RegistryFriendlyTypeName: registryFriendlyTypeName,
            PerceivedType: perceivedType,
            ContentType: contentType,
            RegistryDefaultIcon: registryDefaultIcon,
            RegistryOpenCommand: NotepadRegistryOpenCommand);
    }

    private static BuiltInFileTypeDefinition Open(
        string extension,
        string progId,
        string displayName,
        string knownType,
        BuiltInOpenCommandKind commandKind,
        string? perceivedType,
        string? contentType,
        string registryDefaultIcon,
        string registryOpenCommand)
    {
        return new BuiltInFileTypeDefinition(
            extension,
            displayName,
            knownType,
            BuiltInFileAssociationKind.OpenCommand,
            commandKind,
            RegistryProgId: progId,
            PerceivedType: perceivedType,
            ContentType: contentType,
            RegistryDefaultIcon: registryDefaultIcon,
            RegistryOpenCommand: registryOpenCommand);
    }


    private static BuiltInFileTypeDefinition Script(
        string extension,
        string progId,
        string displayName,
        string knownType,
        BuiltInOpenCommandKind commandKind,
        string? perceivedType,
        string? contentType,
        string registryDefaultIcon)
    {
        return new BuiltInFileTypeDefinition(
            extension,
            displayName,
            knownType,
            BuiltInFileAssociationKind.Script,
            commandKind,
            RegistryProgId: progId,
            PerceivedType: perceivedType,
            ContentType: contentType,
            RegistryDefaultIcon: registryDefaultIcon,
            RegistryOpenCommand: NotepadRegistryOpenCommand);
    }

    private static BuiltInFileTypeDefinition RegistryFile(
        string extension,
        string progId,
        string displayName,
        string knownType,
        string? perceivedType,
        string? contentType,
        string registryDefaultIcon)
    {
        return new BuiltInFileTypeDefinition(
            extension,
            displayName,
            knownType,
            BuiltInFileAssociationKind.RegistryFile,
            BuiltInOpenCommandKind.RegistryMerge,
            RegistryProgId: progId,
            PerceivedType: perceivedType,
            ContentType: contentType,
            RegistryDefaultIcon: registryDefaultIcon,
            RegistryOpenCommand: NotepadRegistryOpenCommand);
    }


    private static BuiltInFileTypeDefinition Executable(
        string extension,
        string displayName,
        string knownType)
    {
        return new BuiltInFileTypeDefinition(
            extension,
            displayName,
            knownType,
            BuiltInFileAssociationKind.Executable);
    }


    private static ExplorerOpenCommand GetRequiredOpenCommand(BuiltInOpenCommandKind commandKind)
    {
        return GetOpenCommand(commandKind)
            ?? throw new InvalidOperationException($"No built-in open command is registered for {commandKind}.");
    }

    private static ExplorerOpenCommand? GetOpenCommand(BuiltInOpenCommandKind commandKind)
    {
        return commandKind switch
        {
            BuiltInOpenCommandKind.SecurityCatalog => SecurityCatalogCommand,
            BuiltInOpenCommandKind.Pkcs7Certificate => Pkcs7CertificateCommand,
            BuiltInOpenCommandKind.CertificateStore => CertificateStoreCommand,
            BuiltInOpenCommandKind.PresentationHost => PresentationHostCommand,
            BuiltInOpenCommandKind.ComExecute => ComExecuteCommand,
            BuiltInOpenCommandKind.PowerShell => PowerShellCommand,
            BuiltInOpenCommandKind.CommandPrompt => CommandPromptCommand,
            BuiltInOpenCommandKind.WindowsScriptHost => WindowsScriptHostCommand,
            BuiltInOpenCommandKind.RegistryMerge => RegistryMergeCommand,
            _ => null
        };
    }

    private static void AddKnownNoDefault(
        Dictionary<string, ExplorerFileAssociation> associations,
        string extension,
        string displayName,
        string knownType)
    {
        Add(associations, new ExplorerFileAssociation(
            extension,
            displayName,
            new ExplorerFileIconIdentity(ExplorerFileIconIdentityKind.KnownType, knownType),
            null,
            [],
            [],
            IsUserDefined: false));
    }

    private static void AddKnownWithDefault(
        Dictionary<string, ExplorerFileAssociation> associations,
        string extension,
        string displayName,
        string knownType,
        ExplorerOpenCommand command)
    {
        Add(associations, new ExplorerFileAssociation(
            extension,
            displayName,
            new ExplorerFileIconIdentity(ExplorerFileIconIdentityKind.KnownType, knownType),
            command,
            [command],
            [],
            IsUserDefined: false));
    }

    private static void AddScript(
        Dictionary<string, ExplorerFileAssociation> associations,
        string extension,
        string displayName,
        string knownType,
        ExplorerOpenCommand? runCommand)
    {
        ExplorerFileVerb[] verbs = runCommand is not null && IsOpenCommandAvailable(runCommand)
            ? [new ExplorerFileVerb(runCommand.Id, $"Run with {runCommand.DisplayName}", runCommand)]
            : [];

        Add(associations, new ExplorerFileAssociation(
            extension,
            displayName,
            new ExplorerFileIconIdentity(ExplorerFileIconIdentityKind.KnownType, knownType),
            NotepadCommand,
            [NotepadCommand],
            verbs,
            IsUserDefined: false));
    }

    private static bool IsOpenCommandAvailable(ExplorerOpenCommand command)
    {
        if (string.IsNullOrWhiteSpace(command.ExecutablePath))
            return false;

        string executablePath = Environment.ExpandEnvironmentVariables(command.ExecutablePath);

        if (executablePath.Equals("%1", StringComparison.OrdinalIgnoreCase))
            return true;

        return File.Exists(executablePath);
    }

    private static void AddKnownEditInNotepad(
        Dictionary<string, ExplorerFileAssociation> associations,
        string extension,
        string displayName,
        string knownType)
    {
        Add(associations, new ExplorerFileAssociation(
            extension,
            displayName,
            new ExplorerFileIconIdentity(ExplorerFileIconIdentityKind.KnownType, knownType),
            NotepadCommand,
            [NotepadCommand],
            [],
            IsUserDefined: false));
    }

    private static void Add(
        Dictionary<string, ExplorerFileAssociation> associations,
        ExplorerFileAssociation association)
    {
        associations[NormalizeExtension(association.Extension)] = association with
        {
            Extension = NormalizeExtension(association.Extension)
        };
    }

    private static string NormalizeExtension(string extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
            return string.Empty;

        extension = extension.Trim();
        return extension.StartsWith('.') ? extension : "." + extension;
    }
}
