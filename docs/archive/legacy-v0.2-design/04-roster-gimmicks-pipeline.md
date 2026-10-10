# 04 — Roster (16) + Gimmicks + Pipeline

**Orden historia→mecánica:** cada gimmick nace del lore, no al revés. Detalle completo en GDD §3.

## Inicio (3)
- **Glen Dantao** Danimal quasi-Gris, Rushdown/Grappler, 950 HP. Lobo Blanco (Rage parcial / Super completo). No ataca máscara Gris. Ficha completa §3.2 GDD / `02-combat-system.md`
- **Kira** Dant, Rushdown técnico, 900 HP. Aceleración: special conectado → dash hasta x3, reset al recibir daño
- **Tom Bomb** Dant, Zoner/Agresivo, 1000 HP. Carga explosiva: 3er hit mismo botón → explosión +50 HP. Finishers lowrider cultural

## Desbloqueables (5)
- **Kamy Litty** 850, Mística/Tricky. Alternancia auto al 40% HP Kamy serena ↔ Súcubo (jugador no controla). Sub-jefa Novato/Pesadilla/Especial
- **E.Coli / Logan** 1200, Tank/Grappler. Super Armor en Rage. DOB proyectil absorbente (reduce EX rival). Sub-jefe Pro
- **BS** 1000, Zoner preciso. Copia último special rival con 8f delay; si repite 2x → copia 150%. Sub-jefe Experto
- **Refugio Benavides** 1050, Cazador/Alquimista. Purificación: −1 s Rage rival por hit. Agua bendita 120% vs Danimal extremo
- **Denim** 950, Zoner puro sin entrenamiento. Hackeo: desactiva gimmick rival 5 s por 2 seg EX. Finishers externos (satélite/robots)

## Ocultos (6)
Tyr (Boxer: LK/MK no dañan, solo spacing; +5% daño/golpe round) · Ellena (asimétrica: derecha full / izquierda −15%, muñón inparryable; aéreos +2f + miniknockback) · Nen (mov semi-aleatorio, daño exacto; globos helio) · Du Lang (3 máscaras D+D+S, gas venenoso; lorea Gris) · Patricia/Lola/K789 (veneno 3 HP/s 10 s; antídoto = rival activa Resonancia) · Ni Troll (150% daño base, hurtbox enano: ataques ≥45° fallan, el más lento)

## Ultra-secretos
- **Num Ba** 1000, tribal, 100% juego. Caza León: 3 hits → marca 8 s → siguiente Heavy unblockable. NPC cine hasta 100%
- **Nad'ian (=Dan'ian anagrama)** 1050, Gris puro. Máscara Equilibrio: Special+Block alterna Dant(aura azul precisión)/Danimal(aura roja fuerza) 25% EX, mid-combo, + técnicas Plano Gris. 101%

## NPCs
Dan'ian enmascarado (Dojo Hub, doble lectura) · Dimitri (mayordomo Krusha, Boss Rush; vence → planos vs Glen) · Wu (solo cinemáticas + pelea especial cadena).

## Pipeline obligatorio frame-first (15–23 días/pj, 6–8 pers)
1 Frame data programador+GD (1d) → 2 Hitboxes validadas en código (1d) → 3 Concepto turnaround 8 ángulos (2–3d) → 4 Sprite base cel 512 (3–4d) → 5 Anim 8–12 base + IA intermedios (5–8d) → 6 Hitbox/hurtbox por frame en Godot (1–2d) → 7 FX auras/impactos/gimmick (2–3d) → 8 Polish hitstop+audio sync <2f (1–2d).
Política IA: solo modelos licencia clara, revisión manual cada frame, propiedad total equipo.
Vidas/estilos v3–v5 antiguos (Dan starter, Ares jugables, Brutus/Mira/Vex/Sol) **descartados** — v8 manda.
