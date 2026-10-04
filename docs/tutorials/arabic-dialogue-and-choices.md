# Arabic dialogue, choices, and RTL typewriter

Write Arabic in normal logical Unicode order in the PO file. Do not reverse
Arabic words or characters. EasyRPG-Arabic handles Arabic shaping and BiDi,
including mixed Arabic, Latin text, and numbers. During the typewriter
animation, Arabic dialogue is revealed from right to left.

For example:

```text
السلام عليكم
HP ديالك 50 / 100
RPG Maker 2003 خدام مزيان
```

Keep Latin text and numbers in their normal order. PO files should remain UTF-8.
For a multiline entry, an internal escaped `\n` is valid and represents a line
break:

```po
msgid "First line\nSecond line"
msgstr "السطر اللول\nالسطر الثاني"
```

Do not add a final trailing `\n` to `msgstr` when the source entry does not
contain one; it can affect translation lookup or message layout.

For interactive dialogue, use RPG Maker's **Show Choices** command for the
actual options. If the player needs a question or prompt first, show it with
**Show Text** before **Show Choices**. Arabic choices receive RTL rendering
automatically; do not manually reverse their text.

If the old RPG Maker editor corrupts Arabic, ASCII placeholders in the editor
are fine. Put the Arabic translation in the generated PO file's `msgstr` field.
