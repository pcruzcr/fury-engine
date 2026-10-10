# ADR-010: Transporte online: canales, relay y código de sala

- **Estado:** Aceptado (2026-10-02). Actualiza ADR-004.
- **Contexto:** ADR-004 preveía relays TURN/STUN y snapshots en delta por la red. El protocolo final es de **inputs** (ventana
  redundante + ack + huella de estado), no de estado, y hacía falta una vía para jugadores tras NAT que no pueden abrir puertos.

## Decisión

1. **`IDatagramChannel`** abstrae cómo se llega al rival (`DirectChannel`: UDP a una dirección conocida; `RelayChannel`: a través de un
   relay). El handshake y `UdpTransport` solo hablan con el canal, así que todas las propiedades de seguridad (claves direccionales
   HKDF, HMAC por paquete, ventana anti-replay, detección de desync) valen igual por ambas vías. El canal se reutiliza del handshake a
   la partida (mismo puerto de origen, necesario para NAT y relay).
2. **El relay es un reenviador tonto** (`RelayServer`, `tools/relay`): empareja a dos jugadores que nombran la misma sala y reenvía
   datagramas ya autenticados de extremo a extremo. Puede perderlos o retrasarlos; no puede leerlos, falsificarlos ni repetirlos. No
   guarda secretos y nunca envía más bytes de los que recibe (sin amplificación). Límites: salas, salas por dirección, inactividad y
   paquetes por segundo por jugador.
3. **Código de sala = secreto compartido.** `RelayRoom` deriva con PBKDF2 el id de sala que ve el relay y un secreto que entra en la
   sal del HKDF del handshake; un relay que no conoce el código obtiene claves distintas y todo su tráfico se rechaza (man-in-the-middle
   fallido). El juego genera códigos de 50 bits (`GenerateCode`).
4. **Un solo anfitrión y un invitado** elegidos por los jugadores; sin matchmaking.

## Consecuencias y límites

- Un relay hostil puede intentar adivinar offline un código corto: por eso se generan y no se escriben a mano.
- **No se ha probado en Internet real** (solo loopback, Windows, Linux y Windows↔Linux en un mismo equipo) y no hay hole punching,
  reconexión, ligas, salas de 8 ni espectadores (ver `docs/ROADMAP.md`, Hito D).
- El handshake compara reglas y personajes pero **no la versión del build**: dos builds con código distinto y los mismos datos solo se
  detectan al desincronizarse. Pendiente: puerta de versión.
