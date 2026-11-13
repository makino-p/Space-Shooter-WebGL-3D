# Space Shooter 3D

3D-шутер в открытом космосе на Unity 6 (6000.6). Полёт вдоль бесконечного энергетического луча,
волны врагов, поток транспортников, финальный босс.

**Играть в браузере:** см. ссылку GitHub Pages в описании репозитория (сборка WebGL лежит в папке `docs/`).

## Управление
| Клавиша | Действие |
|---|---|
| Мышь / стрелки | поворот (клик — захватить мышь, Esc — отпустить) |
| W / S | рычаг газа вверх / вниз, X — полная остановка |
| A / D, Q / E | стрейф влево/вправо, вниз/вверх |
| Space / ЛКМ | огонь (пулемёт) |
| ПКМ (держать) | вращение вокруг своей оси, чем дольше — тем быстрее |
| Left Shift | форсаж (тратит заряд двигателей) |
| C | притянуть бонусы |
| R | рестарт после конца игры |

## Сборка
1. Открыть проект в Unity **6000.6.x** с модулем **Web Build Support**.
2. `Tools > Space Shooter 3D > Build WebGL for GitHub Pages` — сборка появится в `docs/`.

## Что не входит в репозиторий
По лицензиям эти ассеты в git не хранятся — их нужно добавить локально:
- **Star Sparrow Modular Spaceship** (Ebal Studios) и **Federation Corvette F3** — Unity Asset Store,
  импорт через `Window > Package Manager > My Assets`. Без них используются корабли из 2D-спрайтов.
- **NASA Deep Star Map 8K** (`Assets/ThirdParty/NASA/milkyway_2020_8k.exr`, 137 МБ — больше лимита GitHub):
  https://svs.gsfc.nasa.gov/4851
- **Саундтреки** (`Assets/Audio/Soundtracks/`) — сторонняя музыка, только для локального использования.

## Благодарности
- Planet textures: **Solar System Scope** — CC BY 4.0 (https://www.solarsystemscope.com/textures/)
- Star map: **NASA/Goddard Space Flight Center Scientific Visualization Studio** — public domain
- Asteroid scans: **Poly Haven** — CC0
- Ships: Star Sparrow (Ebal Studios), Federation Corvette F3 — Unity Asset Store EULA (только в собранной игре)
