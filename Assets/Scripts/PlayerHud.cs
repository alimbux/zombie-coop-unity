using UnityEngine;

namespace ZombieCoop
{
    /// Интерфейс одной части экрана: прицел, здоровье, патроны, сообщения.
    /// Белый текст; красный только для попаданий и урона.
    public class PlayerHud
    {
        public static readonly Color Blood = new Color(0.9f, 0f, 0.03f);

        public string status = "";
        public string level = "";
        public string message = "";
        public bool headshot;
        float hitTimer;
        float messageTimer;
        float damage;

        public void SetStatus(float hp, string weapon, string ammoText, int kills, bool downed, float revive)
        {
            string ammo = ammoText == "" ? weapon : weapon + "  " + ammoText;
            status = "ЗДОРОВЬЕ " + (int)hp + "     " + ammo + "     УБИТО " + kills;
            if (downed)
            {
                messageTimer = 0.2f;
                message = revive > 0f ? "ПОДНИМАЮТ… " + (int)(revive * 100f) + "%" : "ТЫ ПАЛ — ЖДИ НАПАРНИКА";
            }
        }

        public void SetLevel(string text) { level = text; }

        public void FlashHit(bool head)
        {
            headshot = head;
            hitTimer = 0.15f;
        }

        public void FlashDamage() { damage = 0.45f; }

        public void ShowMessage(string text, float seconds = 2f)
        {
            message = text;
            messageTimer = seconds;
        }

        public void Tick(float delta)
        {
            damage = Mathf.Max(0f, damage - delta * 1.5f);
            if (hitTimer > 0f)
            {
                hitTimer -= delta;
                if (hitTimer <= 0f) headshot = false;
            }
            if (messageTimer > 0f)
            {
                messageTimer -= delta;
                if (messageTimer <= 0f) message = "";
            }
        }

        static GUIStyle style;

        static GUIStyle Style(int size, TextAnchor anchor, Color color)
        {
            if (style == null) style = new GUIStyle(GUI.skin.label) { wordWrap = false, clipping = TextClipping.Overflow };
            style.fontSize = size;
            style.alignment = anchor;
            style.normal.textColor = color;
            return style;
        }

        /// Рисует интерфейс в прямоугольнике части экрана. scale — масштаб от размера 1600×900.
        public void Draw(Rect r, float scale)
        {
            if (damage > 0f)
            {
                GUI.color = new Color(Blood.r, Blood.g, Blood.b, damage);
                GUI.DrawTexture(r, Texture2D.whiteTexture);
                GUI.color = Color.white;
            }
            int S(float px) => Mathf.Max(8, Mathf.RoundToInt(px * scale));
            var cross = new Rect(r.center.x - 30 * scale, r.center.y - 30 * scale, 60 * scale, 60 * scale);
            bool hit = hitTimer > 0f;
            GUI.Label(cross, hit && headshot ? "×" : "+", Style(S(32), TextAnchor.MiddleCenter, hit ? Blood : Color.white));
            GUI.Label(new Rect(r.x + 24 * scale, r.yMax - 70 * scale, r.width, 50 * scale), status, Style(S(22), TextAnchor.MiddleLeft, Color.white));
            GUI.Label(new Rect(r.x + 24 * scale, r.y + 16 * scale, r.width, 34 * scale), level, Style(S(22), TextAnchor.MiddleLeft, Color.white));
            if (message != "")
                GUI.Label(new Rect(r.x, r.y + 60 * scale, r.width, 60 * scale), message, Style(S(34), TextAnchor.MiddleCenter, Color.white));
        }
    }
}
