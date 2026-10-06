# File2Folders

A Windows command-line tool that turns a Netscape-format bookmarks file (as exported by Chrome, Edge or Firefox) into a folder tree of Internet Shortcut (`.URL`) files. C# port of `file2folders.py`.

- **Folders become folders, links become `.URL` files.** The tree is made next to the bookmarks file, in a folder named after it without its extension.
- **Dates are kept.** Each file and folder gets its creation time from the bookmark's `ADD_DATE` and its modified time from `LAST_MODIFIED` (or `ADD_DATE` when there is none). Use `--no-dates` to skip this.
- **Titles are cleaned up into file names.** Characters Windows won't allow (`\ / : * ? " < > |`) become spaces, `�` becomes `-`, runs of spaces become one, `[ ]` become `( )`, YouTube's `▶ ` and leading `(3) ` notification counts are dropped, names are cut to 127 characters, and reserved names like `CON` get a `_` added. A link with no title is named after its host.
- **Re-runs merge.** Existing folders are reused and existing `.URL` files are overwritten. Two links in the same folder whose cleaned-up titles match end up as one file (the later one wins), with a warning.

## Usage

```
File2Folders bookmarksfile [options]
```

| Option | Effect |
|---|---|
| `--no-dates` | Don't set file and folder times from the bookmarks file |
| `-h`, `--help` | Show help |

Exit code is 0 on success, 1 if the bookmarks file can't be read or a folder can't be made, and 2 if some links had problems (listed in the output).

## Publish

Settings are in `Properties\PublishProfiles\FolderProfile.pubxml` (self-contained, single file, trimmed, to `D:\Tools\bin`):

```
dotnet publish File2Folders.csproj -p:PublishProfile=FolderProfile
```
