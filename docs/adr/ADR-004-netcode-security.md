# ADR-004: Red P2P blindada — delta de snapshots, HMAC por paquete, relays TURN/STUN

- **Estado:** Aceptado (2026-09-14)

## Contexto

El netcode rollback transmite estado de simulación entre pares desconocidos. Riesgos:
spoofing de paquetes, replay de mensajes, DoS por exposición de IP, y ancho de banda
desbordado si se transmite el snapshot completo (esquema 5, ~1068 B/frame).

## Decisión

1. **Compresión delta:** `SnapshotDiffer` calcula runs de ints modificados entre frames
   consecutivos y solo transmite esos rangos. Formato: `[start, count, valores…]`.
2. **HMAC-SHA256 por paquete:** cada datagrama lleva secuencia LE + MAC sobre
   `payload || seq`. Verificación en tiempo constante (`CryptographicOperations`).
   Claves de sesión de 256 bits derivadas por sessión (HKDF en el handshake — pendiente
   F2); nunca claves estáticas en el binario.
3. **Anti-replay:** ventana deslizante de 64 secuencias (`PacketReplayGuard`); duplicados
   y paquetes viejos se descartan antes de tocar la sim.
4. **Relays TURN/STUN:** en producción el tráfico competitivo se enruta por relays para
   ocultar IPs de usuarios (anti-DDoS/anti-swatting). El transporte UDP directo es solo
   fallback LAN.
5. **Valores sensibles ofuscados en tránsito:** EX/Resonancia nunca viajan en claro
   independiente — viajan dentro del snapshot hasheado (xxHash64 de verificación por frame).

## Consecuencias

- Overhead por paquete: +36 B (seq + MAC), compensado por deltas típicos de decenas de bytes.
- La rotación SplitMix64→xoshiro256** queda cerrada: el sim usa xoshiro256** nativo con
  seeding SplitMix64 desde F0.

## Actualización (2026-10-09)

- El HKDF por sesión está **implementado** (`SessionCrypto`: ECDH P-256 + HKDF-SHA256, claves direccionales, token opcional).
- El protocolo final transmite **inputs** (ventana redundante + ack + huella de estado cada 30 frames), no snapshots en delta;
  `SnapshotDiffer` existe y se usa en pruebas y comprobaciones, no en la red. EX y Resonancia no viajan: ambos peers simulan.
- En lugar de relays TURN/STUN hay un **relay UDP propio** y un transporte por canales: ver ADR-010.
