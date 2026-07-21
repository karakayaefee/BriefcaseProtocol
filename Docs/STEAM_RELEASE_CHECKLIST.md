# Steam Playtest Release Checklist

## Account and application

- [ ] Complete Steamworks partner onboarding, tax/bank data and Steam Direct fee.
- [ ] Create the base `Briefcase Protocol` AppID.
- [ ] Create the associated `Briefcase Protocol Playtest` AppID.
- [ ] Add at least two administrators and verify publishing permissions.

## Store assets

- [ ] Library capsule, header, small capsule, library hero, logo and community icon.
- [ ] Store descriptions mention only features present in the submitted build.
- [ ] Screenshots are captured from gameplay rather than concept art.
- [ ] Supported languages are English and Turkish.
- [ ] Minimum requirements and microphone/internet requirements are listed.

## Build

- [ ] Run `Briefcase Protocol > Build > Windows Release`.
- [ ] Upload the contents of `Builds/Windows` to the Playtest depot with SteamPipe.
- [ ] Test launch, update, uninstall and reinstall through a private Steam branch.
- [ ] Test four accounts joining via the in-game UGS session code.
- [ ] Keep the previous known-good depot build available for rollback.

## Privacy and support

- [ ] Publish privacy notice for anonymous UGS identity and Vivox voice.
- [ ] State that voice audio is transmitted but not recorded by the game.
- [ ] Add known issues, controls and feedback link.
- [ ] Prepare a password-protected direct ZIP fallback.

Never commit Steam credentials, Steamworks SDK binaries or credential-bearing VDF files to this repository.
