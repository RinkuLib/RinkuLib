# Tracking

Rinku.Tracking provides editable item and list wrappers for ordinary application
objects. The runtime contracts support editing, original-value access, validation,
metadata, dynamic member access, and structural list changes.

The public API is organized under `Rinku.Tracking`. Start with
`TrackingExtensions.ToTrackingItem` for one object or
`TrackingExtensions.ToTrackingList` for a collection. Generated wrappers preserve
the original object until an edit is committed or cancelled.

Tracking is intentionally separate from database persistence: after a successful
save, call the appropriate commit operation to accept the local state.
