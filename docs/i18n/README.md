# Language packs

Built-in UI copy is English, Simplified Chinese, and Japanese. Extra languages and overrides are JSON files the user imports in **Settings → General**.

Export **Export template** for a full file with every key. Translate `name`, set `code` to a BCP 47 tag (`ko`, `fr`, `zh-Hant`, `pt-BR`, …), then import or drop the file onto the language-pack area.

```json
{
  "code": "ko",
  "name": "한국어",
  "fallback": "en",
  "strings": {
    "Settings": "설정",
    "Language": "언어"
  }
}
```

| Field | Meaning |
| --- | --- |
| `code` | Locale id, 2–16 chars (`en`, `zh-Hans`, `ko`, `pt-BR`) |
| `name` | Label in the language list |
| `fallback` | Optional built-in base: `en`, `zh-Hans`, or `ja` |
| `strings` | Map of catalog keys to translated text. Omit keys to keep the fallback |

Same `code` as a built-in language **overrides** that language. A new `code` **adds** a language.

Packs are stored next to the client cache as `locales/<code>.json` (max 256 KiB, 32 packs). They are not synced to the server.
