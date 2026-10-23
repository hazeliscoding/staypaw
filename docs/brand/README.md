# Staypaw brand

The logo is a placeholder: a paw holding a window down. The real one is an owner item in M0 of the [roadmap](../../ROADMAP.md), to be picked in a claude.ai design project.

- `mark.svg` / `mark-dark.svg`: the paw mark, for light and dark backgrounds.
- `lockup.svg` / `lockup-dark.svg`: the mark plus the "staypaw" wordmark, as in the README's title.
- The wordmark is Fredoka SemiBold (SIL Open Font License), lowercase, with -0.01em letter spacing, converted to paths because GitHub can't load web fonts inside SVGs. Use the SVGs; don't re-typeset it. `scripts/make_lockup.py` rebuilds both lockups from the font and the mark, and its header says how. Rerun it whenever the mark changes.
- `app-icon.svg` is the mark on a paper tile, the source of `staypaw.ico` and `app-icon-256.png`. `scripts/make-icons.cs` renders them.

Logo colours: ink `#1d1a16` and rust `#b5601f` on light backgrounds, paper `#f2ede2` and rust `#e8954f` on dark ones.
