# ShareFile Document Index Report

A small Windows desktop app that generates an Excel "Document Index" report for any
client folder in ShareFile — modeled on the Intralinks Document Index report format.

Each person signs in with their own ShareFile email/password, picks a client folder,
and gets an `.xlsx` file.

## Report columns

`Kind | Title | Ownership | ID | Document Size | Folder Path | Added By | Organization | Added On | Modified On | Document Link`

Intralinks-specific columns with no ShareFile equivalent (`Redacted`, `Unread`, `Note`,
`Protection`) were dropped. `Ownership` and `Organization` are best-effort — ShareFile
doesn't always have this data populated on every item, so those cells may be blank.

## One-time setup (whoever sets this up for the firm)

**This repo is public**, so the real Client ID/Secret must never be committed to
`appsettings.json` — that file only holds placeholders and is safe to check in.
Real credentials go in a second file that Git is configured to ignore.

1. **Reuse the existing ShareFile API app** (the one already registered for Cowork) —
   no need to create a new one. You need its **Client ID** and **Client Secret**, plus
   your firm's ShareFile subdomain (the part before `.sharefile.com` in your ShareFile
   URL, e.g. `calderassociates`).
2. In `src/ShareFileDocumentIndex.App/`, copy `appsettings.local.json.example` to a new
   file named **`appsettings.local.json`** (this exact name is in `.gitignore`, so Git
   will never track it), and fill in your real values:
   ```json
   {
     "ShareFile": {
       "Subdomain": "calderassociates",
       "ClientId": "<client id>",
       "ClientSecret": "<client secret>"
     }
   }
   ```
   The app reads `appsettings.json` first, then layers `appsettings.local.json` on top
   if present — so `appsettings.local.json` only needs the fields you're overriding.
   - Leave `RootFolderPath` unset to list folders from each user's ShareFile Home.
   - If all client folders live under one shared parent folder (e.g. a "Clients"
     folder), set `RootFolderPath` to its path, e.g. `"/Clients"`, in either file.
3. Double-check before every commit that `appsettings.local.json` doesn't show up in
   GitHub Desktop's changed-files list. If it ever does, something's wrong with the
   `.gitignore` match — stop and ask before committing.

## Build & run (Visual Studio)

1. Open `ShareFileDocumentIndex.sln` in Visual Studio.
2. Set `ShareFileDocumentIndex.App` as the startup project (should be automatic).
3. Press F5 to run, or **Build > Build Solution**.

This project targets `net8.0-windows` with WPF — if Visual Studio prompts to install
the ".NET desktop development" workload, accept it.

> Note: this app was written and code-reviewed without a Windows/.NET build environment
> available in the session that created it. It should build cleanly in Visual Studio,
> but if you hit a compile error on first build, share the error text and it can be
> fixed quickly.

## Distributing to coworkers (no Visual Studio needed on their machines)

Publish a self-contained single EXE so coworkers can just double-click it:

```
dotnet publish src/ShareFileDocumentIndex.App -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish
```

This produces `publish/ShareFileDocumentIndex.exe`. Copy that file **and** your
`appsettings.local.json` (filled in with your firm's Client ID/Secret) next to it on
each coworker's machine — they only ever need to enter their own ShareFile email and
password. Distribute `appsettings.local.json` directly (e.g. a shared drive, not email)
rather than via this public repo.

## How it works

- Signs in via ShareFile's OAuth2 password grant (each user's own credentials — the
  Client ID/Secret only identify the app, they don't grant access on their own).
- Lists the folders under Home (or `RootFolderPath`) so you can pick a client.
- Recursively walks the selected folder via the ShareFile v3 REST API, collecting
  metadata for every file and subfolder.
- Writes the results to an `.xlsx` file via ClosedXML, formatted like the Intralinks
  report (client name + "As Of" timestamp header, then the data table).

## Known limitations

- Very large client folders (thousands of documents) may take a while, especially with
  "Include document links" checked, since that adds one extra API call per document.
  Uncheck it for a faster run without the `Document Link` column populated.
- `Ownership` currently mirrors `Added By` (the item's creator) — ShareFile's API
  doesn't expose a separate "owner" distinct from creator in a way reliable enough to
  build against sight-unseen. If your ShareFile setup does distinguish them and you
  want that reflected, flag it and this can be refined.
- `Organization` reflects the creator's Company field in ShareFile, which isn't always
  populated; it may be blank for some or all items.
