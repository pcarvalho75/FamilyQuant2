# Security boundary

FamilyQuant treats RAW, FASTA, CSV, `.fcf`, and `.fqproj` inputs as data. Their expensive derived caches are content-keyed with SHA-256.

The legacy SEPro library is an exception: `SeproPckg2.ResultPackage.Load` uses .NET `BinaryFormatter` internally. FamilyQuant cannot replace that serializer without replacing the external SEPro package format. A crafted `.sepr2` can therefore execute code during deserialization. Open `.sepr2` files only when they were produced by a trusted SEPro installation and obtained through a trusted channel. Do not open downloaded, emailed, or otherwise untrusted `.sepr2` packages.

The project keeps `EnableUnsafeBinaryFormatterSerialization` narrowly because the vendored SEPro reader requires it. New FamilyQuant-owned formats use JSON/TSV/ZIP and must not introduce `BinaryFormatter`.

The bundled USEARCH executable is verified byte-for-byte against the embedded payload with SHA-256 before execution. A user-supplied `FAMILYQUANT_USEARCH` override remains an explicit trust decision. Every USEARCH process has a 30-minute safety timeout and is killed if it exceeds that limit.
