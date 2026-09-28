# Battery HID Protocol

The app sends a vendor-defined HID feature report through a LAMZU interface with vendor ID `373E`. It selects an interface with a vendor usage page and a 65-byte Windows feature-report buffer. The target model is LAMZU Maya X (`373E:001E`); the recorded device check was performed on September 3, 2026.

The report contains 64 payload bytes. Windows transfers it in a 65-byte buffer: byte 0 is Report ID `0`, followed by the report payload.

| Payload field | Value | Purpose |
| --- | --- | --- |
| `payload[2]` | `0x02` | Device identifier |
| `payload[3]` | `0x02` | Command length |
| `payload[5]` | `0x83` | Read battery level |

The response is accepted when it has marker `0xA1`, length `0x02`, command `0x83`, a charging flag of `0` or `1`, and a battery percentage from 1 to 100. Zero and malformed responses are treated as invalid reads. The app retries up to four times.

This note covers the battery read used by the app. Similar protocol fields on another device do not establish compatibility. The implementation is in `src/Battery.cs` and `src/Hid.cs`.