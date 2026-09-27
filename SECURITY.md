# Security

Presentation files and images are untrusted input. This preview bounds package expansion, rejects duplicate ZIP parts, disables XML DTDs/resolvers, validates model geometry and identifiers, and rejects oversized raster images. It never executes macros, OLE payloads or remote relationships.

These checks do not constitute an independent security audit. Native image decoding, font parsing, dependency vulnerabilities, excessive resource use and malformed-document edge cases still need ongoing testing. Keep Uno, SkiaSharp and the .NET runtime updated as a compatible bundle. Avoid opening sensitive documents on a shared browser profile.

Recovery files are local, unencrypted application data. Browser storage can be evicted. No cloud authentication, authorization or coauthoring is implemented. Exporting a file can include slide content and notes; inspect content before distributing it.

Report vulnerabilities privately using GitHub's private vulnerability reporting for this repository when available. Do not post sensitive document contents, credentials or exploit payloads in public issues.
