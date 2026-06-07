namespace BlitztextWindows;

public static class PromptLibrary
{
    public const string ProfessionalEmail = """
        Du erhaeltst ein gesprochenes Transkript. Nutze es als Kontext und erstelle daraus eine professionelle E-Mail auf Deutsch.
        Anforderungen:
        - Formuliere klar, freundlich und geschaeftlich
        - Ergaenze eine passende Betreffzeile
        - Strukturiere die E-Mail mit Anrede, Hauptteil und Schluss
        - Wenn Empfaenger, Namen oder Details fehlen, formuliere neutral ohne Platzhalter wie [Name]
        - Behalte die Absicht und Fakten aus dem Transkript bei
        - Gib NUR die fertige E-Mail zurueck, keine Erklaerungen
        """;

    public const string SocialMediaPost = """
        Du erhaeltst ein gesprochenes Transkript. Nutze es als Kontext und erstelle daraus einen modernen Social-Media-Post auf Deutsch.
        Anforderungen:
        - Schreibe praegnanten, natuerlichen Text mit zeitgemaessem Ton
        - Starte mit einem starken Hook
        - Verwende kurze Absaetze und gute Lesbarkeit
        - Fuege ein paar passende Emojis ein, aber nicht ueberladen
        - Fuege 2-5 passende Hashtags hinzu, wenn sie sinnvoll sind
        - Behalte die Kernaussage aus dem Transkript bei
        - Gib NUR den fertigen Post zurueck, keine Erklaerungen
        """;

    public const string Emoji = """
        Du erhaeltst ein gesprochenes Transkript. Gib den Text moeglichst originalgetreu zurueck, aber fuege passende Emojis ein.
        Setze regelmaessig passende Emojis ein, etwa alle 1-2 Saetze.
        Korrigiere offensichtliche Sprach- und Grammatikfehler. Behalte Stil und Bedeutung bei.
        Gib NUR den Text mit Emojis zurueck, keine Erklaerungen.
        """;
}
