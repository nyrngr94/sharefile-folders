# ShareFile Document Index Report

A small Windows desktop app that generates an Excel "Document Index" report for any
client folder in ShareFile — modeled on the Intralinks Document Index report format.

Each person signs in through a real ShareFile sign-in page in their browser (MFA
included), picks a client folder, and gets an `.xlsx` file.

## Report columns

`Kind | Title | Ownership | ID | Document Size | Folder Path | Added By | Organization | Added On | Modified On | Document Link`

Intralinks-specific columns with no ShareFile equivalent (`Redacted`, `Unread`, `Note`,
`Protection`) were dropped. `Ownership` and `Organization` are best-effort — ShareFile
doesn't always have this data populated on every item, so those cells may be blank.

## One-time setup (whoever sets this up for the firm)

**This repo is public**, so the real Client ID/Secret must never be committed to
`appsettings.json` — that file only holds placeholders and is safe to check in.
Real credentials go in a second file that Git is configured to ignore.

1. **Register a new, dedicated ShareFile API app** (don't reuse the Cowork one — this
   app needs a specific Redirect URI that Cowork's registration won't have, and
   ShareFile requires that Redirect URI to be a real `https://` address — it rejects
   `localhost`/`127.0.0.1` outright). In ShareFile, go to "Get an API Key" and create
   an app with:
   - **Redirect URI**: `https://httpbin.org/anything` (must match exactly — this is
     hardcoded in `ShareFileClient.cs` as `ShareFileClient.RedirectUri`). httpbin just
     echoes the request back as JSON, so the authorization code ShareFile appends shows
     up directly on the page for you to copy — no hosting or setup needed.
   - Record the **Client ID** and **Client Secret** it gives you, plus your firm's
     ShareFile subdomain (the part before `.sharefile.com` in your ShareFile URL, e.g.
     `calderassociates`).

   This uses the OAuth2 **authorization code** flow (a real browser sign-in, MFA
   included) rather than a password grant — ShareFile's password grant can't satisfy
   an MFA challenge, so it fails even with correct credentials on MFA-enabled accounts.
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
each coworker's machine — they only ever need to sign in through the browser window
that pops up with their own ShareFile login (MFA included). Distribute
`appsettings.local.json` directly (e.g. a shared drive, not email) rather than via this
public repo.

## How it works

- Clicking "Sign In with ShareFile" opens your default browser to ShareFile's real
  sign-in page (`/oauth/authorize`). Once you sign in (MFA included), ShareFile
  redirects to `https://httpbin.org/anything?code=...`, which displays that request
  back as JSON — copy the value of `"code"` from the `"args"` section and paste it into
  the app, which exchanges it for an access token. The Client ID/Secret only identify
  the app itself, they don't grant access to any data on their own.
- After signing in once, the session (access token + refresh token) is saved to
  `%LOCALAPPDATA%\ShareFileDocumentIndex\session.dat`, encrypted with Windows DPAPI so
  only that Windows user account can decrypt it. On future launches the app tries this
  saved session first and skips straight to the folder picker — no browser, no code to
  paste — refreshing it silently in the background as needed. The sign-in screen only
  reappears if there's no saved session yet, or ShareFile has revoked it.
- Lists the folders under Home (or `RootFolderPath`) so you can pick a client.
- Recursively walks the selected folder via the ShareFile v3 REST API, collecting
  metadata for every file and subfolder.
- Writes the results to an `.xlsx` file via ClosedXML, formatted like the Intralinks
  report (client name + "As Of" timestamp header, then the data table).

## Known limitations

- **`Document Link` does not currently work.** ShareFile's `/Items(id)/Redirection`
  endpoint (used to get a browser link for an item) returns a `500 InternalServerError`
  for this account on both files and folders -- this is a ShareFile-side server error,
  not a bug in this app. "Include document links" is left checked by default and the
  app will still try, but it gives up gracefully after a few consecutive failures and
  leaves the column blank rather than breaking the rest of the report. Worth revisiting
  if ShareFile support ever has an explanation for the 500, or if a "create a Share"
  based approach is wanted instead (a different, heavier API call -- see git history
  around this note for context).
- Very large client folders (thousands of documents) may take a while, especially with
  "Include document links" checked, since that adds one extra API call per document
  until it gives up. Uncheck it to skip that entirely for a faster run.
- `Ownership` reflects ShareFile's `Owner` field, falling back to the creator if an
  item has no distinct owner set.
- `Organization` reflects the creator's Company field in ShareFile, which isn't always
  populated; it may be blank for some or all items.
