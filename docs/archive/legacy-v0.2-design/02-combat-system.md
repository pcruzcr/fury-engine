# 02 — Sistema de Combate

## Base (GDD §4.1)
1v1 · Best of 3 · 80s ranked / 90s casual · 1000 HP base · **60 FPS fijo, 1f = 16.67 ms**.

## Command Normals (Fase 4)
6 botones LP/MP/HP/LK/MK/HK + direccionales. Cada uno con propiedad única vs base:

| Base | F+ (adelante) | B+ (atrás) |
|------|---------------|------------|
| HP | F+HP recto cabeza, posible overhead, +hitstop | B+HP gancho cabeza cancelable a special |
| MP | F+MP recto estómago poke seguro | B+MP gancho hígado alto daño castigable |
| LP | F+LP avanzante | B+LP uppercut anti-air |
| HK | F+HK hook kick launcher → juggle | B+HK crescent, máx daño normal, muy castigable |
| MK | F+MK frontkick poke largo | B+MK hook hígado otro ángulo |
| LK | F+LK hook muslo low avanzante mixup | B+LK kick rápido startup 4f |
| Extra | — | D+B+LK barrida low + KD garantizado · HP+HK agarre inbloqueable · Detrás+HP+HK estrangula 100 |

## Hitstop = simulación (ambos congelan)
Light 4f · Medium 6f · Heavy 10f · EX 12f · Super 20f (+zoom/oscurecer solo presentación) · Counter +2f dorado · Parry flash azul sin stop (stun rival 15f) · KO 12f slow-mo x0.25 solo presentación.

## FSM estados
NEUTRAL · HITSTOP(hitstopFrames) · HITSTUN L 6–8f / M 10–14f / H 16–22f→KNOCKDOWN · BLOCKSTUN · KNOCKDOWN(hard?) · WAKEUP 8f vulnerable · AIRBORNE(jumpVelocity) · JUGGLE 2–3 hits · COUNTER_HIT(+5f, x1.2) · PARRY_SUCCESS 15f punish · RAGE 5–7s · RESONANCE 3s (exGen x0.5) · EXHAUSTION 4s sin EX.

## Prioridad (sin excepciones)
1 Invulnerable (backdash 1–6f, salto 1–3f, super startup) → no conecta · 2 Solo uno ataca → normal · 3 Tipo: M> L, Special>Normal, Super>todo · 4 Mismo tipo → **Clash rebote neutro 12f 0 dmg**; Super vs Super mismo frame → stun épico 20f ambos 0 dmg · 5 Proyectil vs proyectil → ambos desaparecen.

## Escalado + defensa
Combo: 100 / 92 / 84 / 77 / 70 / −8% por hit, **mín 10%**, Super en combo −20% extra. Defensa: alto ← / bajo ↙ (chip 10%, −5% EX si 3+ blocks), Parry S ventana **6f** (éxito +15% EX +10% RES; fallo x1.2), backdash ←← i1–6 (vuln 7–16), tech LP+LK 10f.

## Ficha Glen Dantao (950 HP, Danimal quasi-Gris, Rushdown/Grappler) — resumen
Normales: LP 4f +2 28 · MP 7f −1 55 · HP 12f −6 90 · LK 5f +1 30 low · MK 8f −3 60 AA · HK 13f −8 95 launcher · + Command Normals tabla §3.2 GDD (B+HK 100 máx, D+B+LK KD, agarres 85/100 inbloqueables).
Especiales: Asalto Lobo QCF+P 10f −4 85 (LP corto/HP largo) · Agarre Vacío 360+P 6f 130 inbloq · Aura Lobo DP+K 8f +4 counter 30f refleja x1.5. Super Lobo Blanco QCFx2+P 10f inv 230 KD.
Rutas: BnB LP>MP>SP1 ~175 · Launcher MP>HK>jMP>jHP>SP1 ~280 · Corner LP>F+HP>B+HP>SP2 ~310 · Super punish B+HK>SUPER ~330.
Gimmick: Rage aura roja +vel; Burst manual HP<30% parcial (garras/ojos azules); Super forma completa. **Condicionamiento: no puede atacar máscara Gris.**
