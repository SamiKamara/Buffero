namespace Buffero.App.Infrastructure;

public static class StorageSpaceProbe
{
    public static bool TryGetAvailableFreeBytes(string path, out long availableFreeBytes, out string? failureReason)
    {
        availableFreeBytes = 0;
        failureReason = null;

        try
        {
            var fullPath = Path.GetFullPath(path);
            var root = Path.GetPathRoot(fullPath);
            if (string.IsNullOrWhiteSpace(root))
            {
                failureReason = "Could not resolve a drive root for the configured path.";
                return false;
            }

            var drive = new DriveInfo(root);
            if (!drive.IsReady)
            {
                failureReason = $"Drive {drive.Name} is not ready.";
                return false;
            }

            availableFreeBytes = drive.AvailableFreeSpace;
            return true;
        }
        catch (Exception exception)
        {
            failureReason = exception.Message;
            return false;
        }
    }
}
