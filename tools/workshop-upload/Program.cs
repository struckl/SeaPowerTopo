// Updates the mod's Steam Workshop item through the running Steam client – the same calls the game's
// Mods > Create Mod makes for an existing item (CreateModMenuViewModel.SubmitToWorkshop), minus everything this
// update doesn't touch: title, tags, visibility, dependencies and the compatibility metadata stay as they are on the
// item. Needs Steam running, signed in with the account that owns the item; no password.
//
//   dotnet run --project tools/workshop-upload -- check  <item id>
//   dotnet run --project tools/workshop-upload -- upload <item id> <content folder> <change note file> [description file]
//                                                        [--preview <image>] [--screenshots <folder>]
//   dotnet run --project tools/workshop-upload -- pictures <item id> [--preview <image>] [--screenshots <folder>]
//
// pictures leaves the content, the description and the change notes alone and only replaces the pictures.
// With a description file (BBCode, UTF-8) the item's description is replaced too; without one it stays as it is.
// --preview replaces the item's main picture (JPG/PNG under 1 MB). --screenshots replaces the pictures on the item's
// page: the ones there are removed and every .jpg/.png of the folder added, in file name order.
using Steamworks;

const uint SeaPowerAppId = 1286220;

string preview = Option("--preview"), screenshots = Option("--screenshots");
args = args.Where(a => a != "--preview" && a != "--screenshots" && a != preview && a != screenshots).ToArray();
if (args.Length < 2 || (args[0] == "upload" && args.Length < 4) || !ulong.TryParse(args[1], out ulong itemId))
{
    Console.Error.WriteLine("usage: check <item id> | upload <item id> <content folder> <change note file> [description file] " +
                            "[--preview <image>] [--screenshots <folder>] | pictures <item id> [--preview <image>] [--screenshots <folder>]");
    return 2;
}

// Outside a Steam launch the API takes the app id from the environment (or steam_appid.txt).
Environment.SetEnvironmentVariable("SteamAppId", SeaPowerAppId.ToString());
Environment.SetEnvironmentVariable("SteamGameId", SeaPowerAppId.ToString());
if (!SteamAPI.Init())
{
    Console.Error.WriteLine("SteamAPI.Init failed: is Steam running and signed in with the account that owns Sea Power?");
    return 1;
}

try
{
    var item = new PublishedFileId_t(itemId);
    if (!Details(item, out SteamUGCDetails_t details, out uint pictures))
        return 1;
    Console.WriteLine($"Item {itemId}: \"{details.m_rgchTitle}\", owner {details.m_ulSteamIDOwner}, " +
                      $"visibility {details.m_eVisibility}, {pictures} pictures, updated {DateTimeOffset.FromUnixTimeSeconds(details.m_rtimeUpdated):u}");
    ulong me = SteamUser.GetSteamID().m_SteamID;
    if (details.m_ulSteamIDOwner != me)
    {
        Console.Error.WriteLine($"Signed in as {me}, not the owner of this item - not uploading.");
        return 1;
    }
    if (args[0] == "check")
        return 0;

    bool content = args[0] == "upload";
    if (!content && args[0] != "pictures")
    {
        Console.Error.WriteLine($"unknown command {args[0]}");
        return 2;
    }
    string folder = content ? Path.GetFullPath(args[2]) : null;
    string note = content ? File.ReadAllText(args[3]).Trim() : null;
    if (content && !File.Exists(Path.Combine(folder, "_info.ini")))
    {
        Console.Error.WriteLine($"{folder} has no _info.ini - not a mod folder.");
        return 1;
    }

    UGCUpdateHandle_t update = SteamUGC.StartItemUpdate(new AppId_t(SeaPowerAppId), item);
    if (content && !SteamUGC.SetItemContent(update, folder.Replace("\\", "/")))
    {
        Console.Error.WriteLine("SetItemContent refused the folder.");
        return 1;
    }
    if (content && args.Length > 4)
    {
        string description = File.ReadAllText(args[4], System.Text.Encoding.UTF8);
        SteamUGC.SetItemDescription(update, description);
        Console.WriteLine($"New description: {description.Length} characters.");
    }
    if (preview != null)
    {
        string file = Path.GetFullPath(preview);
        if (new FileInfo(file).Length >= 1024 * 1024 || !SteamUGC.SetItemPreview(update, file.Replace("\\", "/")))
        {
            Console.Error.WriteLine($"Preview {file} refused (Steam takes JPG/PNG under 1 MB).");
            return 1;
        }
        Console.WriteLine($"New preview: {file}");
    }
    if (screenshots != null)
    {
        string[] files = Directory.GetFiles(Path.GetFullPath(screenshots))
            .Where(f => f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => Path.GetFileName(f), StringComparer.Ordinal).ToArray();
        if (files.Length == 0)
        {
            Console.Error.WriteLine($"No .jpg or .png in {screenshots}.");
            return 1;
        }
        // From the last down, so the indices of the ones still to go stay put.
        for (uint i = pictures; i-- > 0;)
            SteamUGC.RemoveItemPreview(update, i);
        foreach (string file in files)
        {
            if (!SteamUGC.AddItemPreviewFile(update, file.Replace("\\", "/"), EItemPreviewType.k_EItemPreviewType_Image))
            {
                Console.Error.WriteLine($"Picture {file} refused.");
                return 1;
            }
        }
        Console.WriteLine($"Pictures: {pictures} removed, {files.Length} added ({string.Join(", ", files.Select(Path.GetFileName))}).");
    }
    SubmitItemUpdateResult_t result = default;
    bool done = false, ioFailure = false;
    var call = CallResult<SubmitItemUpdateResult_t>.Create((r, failed) => { result = r; ioFailure = failed; done = true; });
    call.Set(SteamUGC.SubmitItemUpdate(update, note));
    Console.WriteLine(content ? $"Uploading {folder} ..." : "Uploading the pictures ...");
    DateTime nextReport = DateTime.MinValue;
    while (!done)
    {
        SteamAPI.RunCallbacks();
        if (DateTime.Now >= nextReport)
        {
            EItemUpdateStatus status = SteamUGC.GetItemUpdateProgress(update, out ulong processed, out ulong total);
            Console.WriteLine($"  {status} {processed / 1048576.0:0.0} / {total / 1048576.0:0.0} MB");
            nextReport = DateTime.Now.AddSeconds(5);
        }
        Thread.Sleep(100);
    }
    Console.WriteLine($"Result: {result.m_eResult}{(ioFailure ? " (I/O failure)" : "")}");
    if (result.m_bUserNeedsToAcceptWorkshopLegalAgreement)
        Console.WriteLine("Steam wants the Workshop legal agreement accepted on the item page before it goes live.");
    if (Details(item, out details, out pictures))
        Console.WriteLine($"Item now updated {DateTimeOffset.FromUnixTimeSeconds(details.m_rtimeUpdated):u}, {pictures} pictures");
    return result.m_eResult == EResult.k_EResultOK && !ioFailure ? 0 : 1;
}
finally
{
    SteamAPI.Shutdown();
}

// The value after an option such as --preview, or null.
string Option(string name)
{
    int i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
}

static bool Details(PublishedFileId_t item, out SteamUGCDetails_t details, out uint pictures)
{
    details = default;
    pictures = 0;
    UGCQueryHandle_t query = SteamUGC.CreateQueryUGCDetailsRequest(new[] { item }, 1);
    SteamUGC.SetReturnAdditionalPreviews(query, true);
    SteamUGCQueryCompleted_t completed = default;
    bool done = false, failed = false;
    var call = CallResult<SteamUGCQueryCompleted_t>.Create((r, f) => { completed = r; failed = f; done = true; });
    call.Set(SteamUGC.SendQueryUGCRequest(query));
    DateTime until = DateTime.Now.AddSeconds(30);
    while (!done && DateTime.Now < until)
    {
        SteamAPI.RunCallbacks();
        Thread.Sleep(100);
    }
    bool ok = done && !failed && completed.m_eResult == EResult.k_EResultOK && completed.m_unNumResultsReturned > 0
              && SteamUGC.GetQueryUGCResult(query, 0, out details) && details.m_eResult == EResult.k_EResultOK;
    if (ok)
        pictures = SteamUGC.GetQueryUGCNumAdditionalPreviews(query, 0);
    SteamUGC.ReleaseQueryUGCRequest(query);
    if (!ok)
        Console.Error.WriteLine($"Could not read item {item.m_PublishedFileId} ({(done ? completed.m_eResult.ToString() : "timeout")}).");
    return ok;
}
