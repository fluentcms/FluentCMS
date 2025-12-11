namespace FluentCMS.Api.Plugins.AIAgentManagement;

public class Tools(ILogger<Tools> logger)
{
    [Description("Request to read the contents of a file at the specified path. Use this when you need to examine the contents of an existing file you do not know the contents of, for example to analyze code, review text files, or extract information from configuration files. Automatically extracts raw text from PDF and DOCX files. May not be suitable for other types of binary files, as it returns the raw content as a string. Do NOT use this tool to list the contents of a directory. Only use this tool on files.")]
    public string ReadFile([Description("The path of the file to read")] string path)
    {
        logger.LogInformation("ReadFile called with path: {Path}", path);
        var fullPath = path;
        if (!File.Exists(fullPath))
        {
            logger.LogWarning("File not found: {FullPath}", fullPath);
            return $"Error: File at path '{fullPath}' does not exist.";
        }
        logger.LogInformation("Reading file: {FullPath}", fullPath);
        return File.ReadAllText(fullPath);
    }

    [Description("Request to write content to a file at the specified path. If the file exists, it will be overwritten with the provided content. If the file doesn't exist, it will be created. This tool will automatically create any directories needed to write the file.")]
    public void WriteFile(
        [Description("The path of the file to write to")] string path,
        [Description("The content to write to the file. ALWAYS provide the COMPLETE intended content of the file, without any truncation or omissions. You MUST include ALL parts of the file, even if they haven't been modified.")] string content)
    {
        logger.LogInformation("WriteFile called with path: {Path}", path);
        var fullPath = path;
        var directory = Path.GetDirectoryName(fullPath);
        if (!Directory.Exists(directory))
        {
            logger.LogInformation("Creating directory: {Directory}", directory);
            Directory.CreateDirectory(directory);
        }
        logger.LogInformation("Writing to file: {FullPath}", fullPath);
        File.WriteAllText(fullPath, content);
    }

    [Description("Request to list files and directories within the specified directory. If recursive is true, it will list all files and directories recursively. If recursive is false or not provided, it will only list the top-level contents. Do not use this tool to confirm the existence of files you may have created, as the user will let you know if the files were created successfully or not.")]
    public List<string> ListFiles(
       [Description("The path of the directory to list contents for")] string directoryPath,
       [Description("Whether to list files recursively. Use true for recursive listing, false or omit for top-level only.")] bool recursive = false)
    {
        logger.LogInformation("ListFiles called with directoryPath: {DirectoryPath}, recursive: {Recursive}", directoryPath, recursive);
        var fullPath = directoryPath;
        if (!Directory.Exists(fullPath))
        {
            logger.LogWarning("Directory not found: {FullPath}", fullPath);
            return [$"Error: Directory at path '{fullPath}' does not exist."];
        }
        logger.LogInformation("Listing files in directory: {FullPath}", fullPath);
        var searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        var entries = Directory.GetFileSystemEntries(fullPath, "*", searchOption).ToList();
        return entries;
    }
}
