# zolsi.cc

stealth autoclicker and input recorder written in c# (.net 9), compiled to native machine code via native aot. features stream-proof console rendering, low-level input hooks, anti-debug checks, and encrypted cloud profiles synced to supabase.

## requirements

- windows 10 or 11 (x64)
- .net 9 sdk
- c++ desktop development workload (msvc build tools, required by native aot)

## database setup

the project uses supabase for pin authentication, cloud profiles, click records, and build blacklisting.

1. create a supabase project.
2. open the supabase sql editor and run `sql/schema.sql`. this creates the tables (`accounts`, `click_records`, `blacklisted_builds`) and sets up permissions.
3. run `sql/create_user.sql` to add an initial account. edit the username and pin in the query before executing if you want something other than the default test user.

## configuration

server endpoints and keys are stored as obfuscated byte arrays in `src/Crypto/StringEncryptor.cs` so they do not show up as plaintext strings in the binary.

replace the example byte arrays in `src/Crypto/StringEncryptor.cs` with your own:

- `EncAccounts`: your supabase accounts url (`https://<project-ref>.supabase.co/rest/v1/accounts`)
- `EncRecords`: your supabase click records url (`https://<project-ref>.supabase.co/rest/v1/click_records`)
- `EncBlacklist`: your supabase blacklisted builds url (`https://<project-ref>.supabase.co/rest/v1/blacklisted_builds`)
- `EncSecret`: your supabase service role or secret key
- `EncDiscordWebhook`: your discord webhook url for security alerts

to generate the obfuscated byte array for any string, run this in powershell:

```powershell
$s = "your_url_or_key_here"
$b = [System.Text.Encoding]::UTF8.GetBytes($s)
for ($i = 0; $i -lt $b.Length; $i++) { $b[$i] = $b[$i] -bxor (0x5B + ($i % 23)) }
($b | ForEach-Object { '0x{0:X2}' -f $_ }) -join ', '
```

paste the output into the corresponding byte array in `StringEncryptor.cs`.

## building

run `build.bat` in the project root, or publish manually from terminal:

```cmd
dotnet publish -c Release
```

the standalone native executable will be placed in `publish\launcher.exe`.

`build.bat` will also:
- detect vmprotect or upx if installed on your system and apply packing.
- hash the output binary with sha-256 and generate a record inside the `hashes\` folder for supabase build blacklist tracking.

## usage

1. launch `publish\launcher.exe`.
2. enter your 6-digit pin at the prompt to authenticate against your supabase database.
3. controls:
   - navigate menu items with arrow keys or mouse hover.
   - press enter to select an option, esc to return to the previous screen.
   - in the clicker menu, configure cps, jitter, and set a toggle hotkey.
   - in the record menu, record custom click patterns and timing sequences to cloud profiles.
   - in settings, toggle stream-proof mode (hides the console window from screen recorders and capture software) and always-on-top.