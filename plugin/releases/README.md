# Plugin release output

`testing.zip` is the local Kupo Skribbl plugin package. `repo.json` is the live
custom installer feed. Internal package/configuration names remain `Kupopop`
so updates preserve installation and device data.

Build-Release.ps1 creates a candidate feed under its output folder. GitHub Actions
uploads and verifies the release ZIP before promoting the live feed. Never
advertise an asset that has not yet been uploaded.
