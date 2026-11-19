# Fixture golden del protocollo

File binari scritti **a mano** (non generati dal codice) e usati sia dai test C# (`host/tests/FreeExtend.Protocol.Tests`) sia da quelli Kotlin (`android/app/src/test`). Se uno dei due lati li legge in modo diverso, il test fallisce: è il controllo che C# e Kotlin si capiscano.

Formato, big-endian: intestazione di 16 byte `u8 type | u8 flags | u16 0 | u32 payloadLength | u64 timestampUs`, poi il payload.

| File | Messaggio | Byte (hex) |
|------|-----------|------------|
| `hello.bin` | `Hello` (tipo 1), flag 0, ts 0, payload `u16 2304, u16 1440, u16 60, u8 codecMask=1` | `01 00 00 00 00 00 00 07 00 00 00 00 00 00 00 00 09 00 05 A0 00 3C 01` |
| `video_key.bin` | `Video` (tipo 3), flag keyframe (bit 0), ts 1234567 (0x12D687), payload `00 00 00 01 65 AA` | `03 01 00 00 00 00 00 06 00 00 00 00 00 12 D6 87 00 00 00 01 65 AA` |

Tipi: 1 Hello, 2 Config, 3 Video, 4 Ping, 5 Pong, 6 Rendered, 7 RequestKeyframe. Payload massimo 8 MiB. Ogni connessione inizia con i 4 byte ASCII `FXD1` inviati da entrambi i lati.
