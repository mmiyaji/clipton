# Clipboard History Persistence Performance

Measured on Windows, .NET Release build, using Windows DPAPI (`DataProtectionScope.CurrentUser`).
Each scenario used 200 clipboard history items.

- `text`: 200 text items, each around 512 characters.
- `mixed`: 180 text items and 20 image items, each image payload around 64 KiB.
- Timings are median values in milliseconds.
- `initial_save_ms`: first save into an empty store.
- `load_ms`: median of 9 loads after warmup.
- `append_save_ms`: save after adding one new item and trimming the oldest item.

## Results

| Scenario | Store | Initial save | Load | Append save | Size |
| --- | ---: | ---: | ---: | ---: | ---: |
| text | legacy single file | 10.93 ms | 10.55 ms | 10.74 ms | 147,894 bytes |
| text | segmented base/delta | 13.24 ms | 10.70 ms | 4.36 ms | 156,147 bytes |
| mixed | legacy single file | 15.95 ms | 16.22 ms | 14.74 ms | 1,885,302 bytes |
| mixed | segmented base/delta | 15.89 ms | 15.53 ms | 3.50 ms | 1,893,595 bytes |

## Summary

The previous store rewrote and re-encrypted the entire history file for every persisted change.
The segmented store keeps:

- `history/manifest.dat`: ordered item IDs and segment membership.
- `history/base.dat`: compacted encrypted history snapshot.
- `history/delta.dat`: encrypted recent additions since the last compaction.

This preserves comparable initial load performance while making normal append saves substantially cheaper:

- Text append save: 10.74 ms to 4.36 ms, about 59% faster.
- Mixed append save: 14.74 ms to 3.50 ms, about 76% faster.

The storage size increases slightly because there are multiple files and a manifest, but the overhead is small compared with the performance benefit for frequent clipboard captures.

## Migration

If only the legacy `%APPDATA%/Clipton/history.dat` exists, the store loads it, writes the segmented format, then moves the legacy file to `history.dat.legacy.bak`.

## Notes

An item-per-file prototype was not adopted. It improved some append cases, but it made startup load significantly worse because each item required a separate DPAPI decrypt. The final base/delta design keeps startup to at most two encrypted payload reads plus a small manifest.
