# Publisher signing and update trust

Status: support implemented; no publisher identity has been configured and no signed release has been produced or accepted.

An Authenticode certificate identifies the publisher of a Windows executable. Keep its private key in the certificate provider/token/service; never commit a private key or password. A public certificate fingerprint is not a secret. The updater compares the signer certificate's SHA-256 fingerprint with the fingerprint compiled into its installed core, after Windows validates the signature and trust chain. A checksum downloaded from the same release does not independently authenticate a publisher.

Unsigned local development and private validation packages still build. Without a publisher pin, updates trust the configured GitHub repository and verify its release checksums and package metadata. Downloaded updates are verified before any embedded setup inspection executes, and verified again before handoff. No release metadata or editable user setting can select the trusted publisher. This does not authenticate the initial installation; obtain that first release from a trusted channel and check its Windows signature.

## Once a code-signing identity is available

1. Arrange a Windows-trusted code-signing certificate and a supported private-key provider. A self-signed test certificate is not a public publisher identity; do not add a test root to users' trust stores.
2. Make that identity available in the current user's My certificate store for SignTool. The implemented packaging adapter uses that store; a hosted service requiring its own signing adapter needs a separate integration.
3. Obtain the certificate's 40-character store thumbprint and 64-character SHA-256 certificate fingerprint. Use the latter for trust pinning; the former only selects the signing certificate. Provide an HTTPS RFC 3161 timestamp endpoint supported by the provider.
4. Run `Package.ps1 -Release -SigningCertificateThumbprint <store-thumbprint> -PublisherCertificateSha256 <certificate-sha256> -SignTool <signtool-path> -TimestampServer <https-endpoint>`.
5. Run `scripts/testing/Test-Distribution.ps1 -RequireSigned`, then the isolated Windows acceptance checklist. Package creation is not publication or production acceptance.

Release packaging refuses missing or inconsistent identity before creating output. It compiles the pin into all core copies, signs first-party payload binaries before hashing/embedding them, and signs both completed installers before computing release checksums. Identical payload binaries reuse identical signed bytes across editions. It verifies Windows trust, exact publisher and signing timestamp. The graphical build timestamp and supporter-testing assets remain excluded from public frontend builds. A cryptographic signing timestamp is separate: it belongs to the signature, not the app interface.

When publisher pinning is configured, the updater requires a trusted chain and cached revocation evidence; offline/unknown revocation fails closed. A real signed fixture and online/offline acceptance remain pending. Certificate renewal/rotation needs a planned transition supported by the previously installed pin; do not replace the signing identity and assume existing installed versions will accept it.

References: [Microsoft SignTool](https://learn.microsoft.com/en-us/windows/win32/seccrypto/signtool), [Authenticode timestamping](https://learn.microsoft.com/en-us/windows/win32/seccrypto/time-stamping-authenticode-signatures).

## Repository-trusted updates (November 7)
Unsigned builds now support in-app updates from the configured GitHub repository. The updater retains HTTPS URL/redirect restrictions, release size and SHA-256 checks, executable version checks, edition/protocol/payload inspection, and revalidation before explicit setup handoff. Repository control is the trust source for unsigned packages; release checksums are integrity checks, not independent publisher authentication. Windows may display Unknown publisher. When a publisher pin is compiled into a build, signature verification remains mandatory; it never falls back to unsigned updates. Only published stable releases are selected by the current latest-release channel.
