# DzlDevTools — attribution & license

DzlDevTools is an **adaptation of DayZ Community Offline Mode (COM)** by
Paul-Eric Lange (**Arkensor**) — <https://github.com/Arkensor/DayZCommunityOfflineMode>.

COM's client framework (module manager, object editor, camera tools, debug monitor,
persistency helpers) was ported into an in-game mod and updated for DayZ 1.29. The
offline character-spawn bootstrap that lives in a mission's `init.c` is dzl's own.

## License

Because it derives from COM, DzlDevTools is licensed under the **same** terms:

> **Creative Commons Attribution-NonCommercial-ShareAlike 4.0 International
> (CC BY-NC-SA 4.0)** — see `LICENSE`.

This means, for this mod and any redistribution of it:

- **Attribution** — credit Arkensor (and dzl) as above.
- **NonCommercial** — no selling it or bundling it into a paid product.
- **ShareAlike** — derivatives must stay under CC BY-NC-SA 4.0.

dzl itself is a separate, free (non-commercial) tool; shipping DzlDevTools alongside it
is within the NonCommercial term. The mod is unsigned and unbinarized on purpose — edit
the source under `source/` and rebuild it (`dzl build DzlDevTools --no-binarize`).
