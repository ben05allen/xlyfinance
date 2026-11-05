# Security policy

## Reporting a vulnerability

Please do **not** open a public issue for anything that could be a security problem.

Report it privately instead:

- GitHub → the **Security** tab → **Report a vulnerability** (private advisory, reaches the maintainer).
- If that is unavailable, contact the maintainer directly through GitHub (`ben05allen`) and do not include
  details in a public issue or pull request.

You should get a response within a few days. Please include enough to reproduce the problem (what you
ran, what you expected, what happened) and a rough timeline so a fix can be prioritised.

## Scope

XlyFinance is a client-side Excel add-in: it reads public Yahoo Finance data over HTTPS, keeps no server,
stores no credentials and writes nothing outside the workbook it is used from. Realistic issues would be
things like injection through formula arguments, unsafe deserialisation of HTTP payloads, or an
unrestricted outbound request built from user input.

Note that Yahoo's `v8/finance/chart` endpoint is an unofficial source; problems with Yahoo's own service
are out of scope here.

## Supported versions

Only the latest commit on `main` is supported — there are no versioned releases yet.