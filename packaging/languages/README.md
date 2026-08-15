# Inno Setup language source

The 19 non-English `.isl` files in this directory are build-time installer translations copied without modification from the official Inno Setup source repository. English is provided by Inno Setup's built-in `Default.isl`.

```text
Repository: https://github.com/jrsoftware/issrc
Path: Files/Languages/*.isl
Upstream commit inspected: 9e1c9960af0dfbabce635ff8250f2958ec312254
Maintainers: recorded in each individual language file
```

The translations are compiled into `VRC-Fisher-Setup-x64.exe`. End users do not download these files or any separate language pack. When updating them, preserve the upstream headers and verify the Setup with the repository's pinned Inno Setup major version.
