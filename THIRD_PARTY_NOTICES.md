# Third-party notices

PNG Sequence Video Forge v1.3.2 embeds an FFmpeg executable and extracts it to
the current user's local application data directory when video conversion is
first used. The application starts FFmpeg as a separate process.

## FFmpeg

- Project: FFmpeg
- Website: https://ffmpeg.org/
- Legal information: https://ffmpeg.org/legal.html
- Source code: https://github.com/FFmpeg/FFmpeg
- Build provider: https://github.com/BtbN/FFmpeg-Builds
- Exact build release: https://github.com/BtbN/FFmpeg-Builds/releases/tag/autobuild-2026-07-18-13-13
- Exact archive: https://github.com/BtbN/FFmpeg-Builds/releases/download/autobuild-2026-07-18-13-13/ffmpeg-n8.1.2-22-g94138f6973-win64-lgpl-8.1.zip
- Build tag: `autobuild-2026-07-18-13-13`
- Archive: `ffmpeg-n8.1.2-22-g94138f6973-win64-lgpl-8.1.zip`
- Archive SHA-256: `268F45C3D6D17718BB84E3B0A7F3155D966D4B65F2FE8D059C8598A38BBE01FD`
- Executable SHA-256: `9203AD8B3940926730575C9EE0845B5FEDCA59EC39C1D3A6161F4F6417A07C1A`
- Variant: `lgpl`
- License: GNU Lesser General Public License; see the exact license text included in this repository

The build is obtained from the pinned release above by `prepare-ffmpeg.ps1`.
The archive and extracted executable are SHA-256 checked before use. The
corresponding FFmpeg source and build scripts are available from the linked
FFmpeg and BtbN repositories. A copy of the license is included as
`FFMPEG-LICENSE.txt`.

FFmpeg is a trademark of Fabrice Bellard, originator of the FFmpeg project.
FFmpeg and its components remain subject to their own copyright and license
terms. This notice does not grant rights to third-party trademarks or codecs.
