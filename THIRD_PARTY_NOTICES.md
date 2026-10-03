# Third-party components

The application is distributed under the GNU General Public License version 3; see LICENSE.

- [Klipper](https://github.com/Klipper3d/klipper): firmware source, build conventions and generator/protocol formats. GPL-3.0. Firmware source is downloaded at runtime, not bundled in the release ZIP.
- [Katapult](https://github.com/Arksine/katapult): bootloader source and protocol. GPL-3.0. Firmware source is downloaded at runtime. SSH operations use the Linux host's installed flashtool with temporary operation guards.
- [xPack GNU Arm Embedded GCC](https://github.com/xpack-dev-tools/arm-none-eabi-gcc-xpack): external compiler, 15.2.1-1.1. The release contains a file-selected subset of the official Windows x64 distribution. Upstream license notices and distro/source-build information are retained inside the toolchain directory. The original download URL and SHA256 are pinned in assets/manifest.json.
- [SSH.NET](https://github.com/sshnet/SSH.NET): SSH/SFTP transport. MIT. Its managed dependencies include Bouncy Castle and Microsoft support libraries.
- [SharpZipLib](https://github.com/icsharpcode/SharpZipLib): firmware dictionary decompression. MIT.
- Microsoft .NET support libraries: licenses and third-party notices are copied from the restored NuGet packages into the release's licenses directory.

The application does not bundle a Python or .NET runtime. The .NET Framework reference assemblies are build-time dependencies only. Each component retains its own license; this file does not replace those license texts.
