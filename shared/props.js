export const DEFAULT_PROPS = [
  // Interactive Lockers
  { id: 'locker-1', type: 'locker', x: -3.4, y: 0, z: 3.5, rotation: Math.PI, exitX: -3.4, exitZ: 2.1, label: 'Шафа в архіві' },
  { id: 'locker-2', type: 'locker', x: -3.4, y: 0, z: -21.5, rotation: Math.PI, exitX: -3.4, exitZ: -22.8, label: 'Шафа в генераторній' },
  { id: 'locker-3', type: 'locker', x: 3.4, y: 0, z: -36.5, rotation: 0, exitX: 3.4, exitZ: -35.2, label: 'Шафа в серверній' },
  { id: 'locker-4', type: 'locker', x: -2.0, y: 0, z: -12.0, rotation: Math.PI / 2, exitX: -1.0, exitZ: -12.0, label: 'Шафа в коридорі' },
  { id: 'locker-5', type: 'locker', x: -11.0, y: 0, z: -46.5, rotation: Math.PI / 2, exitX: -9.8, exitZ: -46.5, label: 'Шафа на техскладі' },
  { id: 'locker-6', type: 'locker', x: 11.0, y: 0, z: -46.5, rotation: -Math.PI / 2, exitX: 9.8, exitZ: -46.5, label: 'Шафа в диспетчерській' },

  // Desks & Workbenches
  { id: 'table_archive', type: 'archivistDesk', x: -8, y: 0, z: -3, w: 2.4, d: 1.3, rotation: 0, label: 'Стіл архіваріуса з лампою' },
  { id: 'table_apparatus', type: 'mixingConsole', x: 10, y: 0, z: -2, w: 2.6, d: 1.4, rotation: Math.PI / 2, label: 'Пульт прямого ефіру' },
  { id: 'table_studio', type: 'anchorDesk', x: 8, y: 0, z: -16.5, w: 3.4, d: 1.4, rotation: 0, label: 'Дикторський стіл новин' },
  { id: 'table_vent', type: 'table', x: -8, y: 0, z: -26.5, w: 2.2, d: 1.2, rotation: 0, label: 'Верстак у вентвузлі' },
  { id: 'table_server', type: 'table', x: 10.5, y: 0, z: -31.5, w: 2.2, d: 1.2, rotation: Math.PI / 2, label: 'Термінал серверної' },
  { id: 'table_tech', type: 'table', x: -8, y: 0, z: -50.5, w: 2.4, d: 1.3, rotation: 0, label: 'Стіл техскладу' },
  { id: 'table_dispatch', type: 'table', x: 8.5, y: 0, z: -46.5, w: 3.6, d: 1.5, rotation: Math.PI, label: 'Пульт диспетчера' },

  // Chairs
  { id: 'chair_archive', type: 'chair', x: -8, y: 0, z: -1.92, rotation: Math.PI, label: 'Стілець архіву' },
  { id: 'chair_studio', type: 'chair', x: 8, y: 0, z: -17.8, rotation: 0, label: 'Стілець студії' },
  { id: 'chair_tech', type: 'chair', x: -8, y: 0, z: -49.5, rotation: 0, label: 'Стілець техскладу' },

  // Archive Shelving & Catalogs
  { id: 'shelf_arch_1', type: 'shelf', x: -11.45, y: 0, z: -1.5, rotation: Math.PI / 2, label: 'Стелаж архіву 1' },
  { id: 'shelf_arch_2', type: 'shelf', x: -11.45, y: 0, z: 1.05, rotation: Math.PI / 2, label: 'Стелаж архіву 2' },
  { id: 'shelf_arch_3', type: 'shelf', x: -7.5, y: 0, z: -6.52, rotation: 0, label: 'Стелаж архіву 3' },
  { id: 'shelf_arch_4', type: 'shelf', x: -10.15, y: 0, z: -6.52, rotation: 0, label: 'Стелаж архіву 4' },
  { id: 'card_catalog_1', type: 'cardCatalog', x: -11.2, y: 0, z: -6.5, rotation: 0, label: 'Каталожна шафа архіву' },
  { id: 'vhs_stack_1', type: 'vhsStack', x: -6.0, y: 0, z: -6.2, rotation: 0.1, label: 'Коробки Betacam 1' },
  { id: 'vhs_stack_2', type: 'vhsStack', x: -10.8, y: 0, z: 3.5, rotation: -0.15, label: 'Коробки Betacam 2' },

  // Tech Storage Shelving
  { id: 'shelf_tech_1', type: 'shelf', x: -11.45, y: 0, z: -46.5, rotation: Math.PI / 2, label: 'Стелаж техскладу 1' },
  { id: 'shelf_tech_2', type: 'shelf', x: -11.45, y: 0, z: -44.0, rotation: Math.PI / 2, label: 'Стелаж техскладу 2' },
  { id: 'shelf_tech_3', type: 'shelf', x: -7.5, y: 0, z: -52.5, rotation: 0, label: 'Стелаж техскладу 3' },
  { id: 'shelf_tech_4', type: 'shelf', x: -10.15, y: 0, z: -52.5, rotation: 0, label: 'Стелаж техскладу 4' },

  // Server Racks & Bundles
  { id: 'rack_1', type: 'serverRack', x: 6.5, y: 0, z: -34.5, rotation: 0, label: 'Серверна стійка 1' },
  { id: 'rack_2', type: 'serverRack', x: 8.7, y: 0, z: -34.5, rotation: 0, label: 'Серверна стійка 2' },
  { id: 'rack_3', type: 'serverRack', x: 10.9, y: 0, z: -34.5, rotation: 0, label: 'Серверна стійка 3' },
  { id: 'rack_4', type: 'serverRack', x: 6.5, y: 0, z: -28.5, rotation: Math.PI, label: 'Серверна стійка 4' },
  { id: 'rack_5', type: 'serverRack', x: 8.7, y: 0, z: -28.5, rotation: Math.PI, label: 'Серверна стійка 5' },
  { id: 'rack_6', type: 'serverRack', x: 10.9, y: 0, z: -28.5, rotation: Math.PI, label: 'Серверна стійка 6' },
  { id: 'cable_bundle_1', type: 'cableBundle', x: 8.5, y: 0, z: -31.5, rotation: 0, label: 'Кабельні джгути 1' },
  { id: 'cable_bundle_2', type: 'cableBundle', x: 8.5, y: 0, z: -35.5, rotation: 0, label: 'Кабельні джгути 2' },
  { id: 'punchcards_1', type: 'debris', x: 10.5, y: 0, z: -33.0, label: 'Перфокарти та документація' },

  // Studio 04 Equipment
  { id: 'chroma_1', type: 'chromakeyBackdrop', x: 8.0, y: 0, z: -22.8, rotation: 0, label: 'Хромакейний павільйон' },
  { id: 'cam_1', type: 'camera', x: 5.0, y: 0, z: -13.5, rotation: Math.PI * 0.96, label: 'Камера ТК-04 №1' },
  { id: 'cam_2', type: 'camera', x: 10.7, y: 0, z: -13.5, rotation: Math.PI * 1.03, label: 'Камера ТК-04 №2' },
  { id: 'mic_stand_1', type: 'micStand', x: 6.6, y: 0, z: -16.0, rotation: 0, label: 'Мікрофонна стійка 1' },
  { id: 'mic_stand_2', type: 'micStand', x: 9.4, y: 0, z: -16.0, rotation: 0, label: 'Мікрофонна стійка 2' },
  { id: 'spot_1', type: 'ceilingSpotlight', x: 6.0, y: 0, z: -14.5, rotation: 0, label: 'Освітлювальний софіт 1' },
  { id: 'spot_2', type: 'ceilingSpotlight', x: 10.0, y: 0, z: -14.5, rotation: 0, label: 'Освітлювальний софіт 2' },

  // Generator Room Units
  { id: 'diesel_gen_1', type: 'dieselGenerator', x: -7.5, y: 0, z: -21.6, rotation: 0, label: 'Дизель-генератор ДГ-200' },
  { id: 'fuel_valve_1', type: 'fuelValve', x: -11.6, y: 0, z: -22.0, rotation: Math.PI / 2, label: 'Аварійний вентиль палива' },
  { id: 'fuel_canister_1', type: 'fuelCanister', x: -10.5, y: 0, z: -12.5, rotation: 0.3, label: 'Каністра солярки 20л' },
  { id: 'fuse_panel_1', type: 'fusePanel', x: -11.75, y: 0, z: -16.5, rotation: Math.PI / 2, label: 'Щит 380В з гніздами' },

  // Ventilation Room Elements
  { id: 'air_duct_1', type: 'airDuct', x: -8.5, y: 0, z: -35.0, rotation: 0, label: 'Вентиляційний короб 1' },
  { id: 'air_duct_2', type: 'airDuct', x: -5.0, y: 0, z: -35.0, rotation: 0, label: 'Вентиляційний короб 2' },
  { id: 'vent_grate_1', type: 'ventGrate', x: -11.75, y: 0, z: -34.0, rotation: Math.PI / 2, label: 'Вентрешітка шахти' },
  { id: 'vent_debris_1', type: 'debris', x: -7.0, y: 0, z: -30.0, label: 'Вентиляційне сміття' },

  // Apparatus & Dispatcher Controls
  { id: 'oscilloscope_1', type: 'oscilloscope', x: 10.6, y: 0.85, z: -3.2, rotation: -Math.PI / 2, label: 'Осцилограф ефіру' },
  { id: 'monitor_wall_1', type: 'monitorWall', x: 8.5, y: 0, z: -52.5, rotation: Math.PI, label: 'Стійка відеоконтролю' },
  { id: 'crt_1', type: 'crt', x: 7.5, y: 1.3, z: -46.7, scale: 0.9, rotation: 0, label: 'Монітор диспетчера 1' },
  { id: 'crt_2', type: 'crt', x: 9.5, y: 1.3, z: -46.7, scale: 0.9, rotation: 0, label: 'Монітор диспетчера 2' },
  { id: 'acoustic_1', type: 'acousticPanel', x: 11.82, y: 0, z: 1.0, rotation: -Math.PI / 2, label: 'Акустична панель 1' },
  { id: 'acoustic_2', type: 'acousticPanel', x: 11.82, y: 0, z: -5.0, rotation: -Math.PI / 2, label: 'Акустична панель 2' },
  { id: 'acoustic_3', type: 'acousticPanel', x: 11.82, y: 0, z: -43.0, rotation: -Math.PI / 2, label: 'Акустична панель 3' },

  // Main Corridor & Hall Elements
  { id: 'turnstile_main', type: 'turnstile', x: 0, y: 0, z: 4.2, rotation: 0, label: 'Турнікети входу' },
  { id: 'honor_board_1', type: 'honorBoard', x: -2.36, y: 0, z: 1.5, rotation: Math.PI / 2, label: 'Дошка пошани працівників' },
  { id: 'schedule_board_1', type: 'scheduleBoard', x: 2.36, y: 0, z: 1.5, rotation: -Math.PI / 2, label: 'Сітка мовлення' },
  { id: 'fire_board_1', type: 'fireBoard', x: -2.36, y: 0, z: -20.0, rotation: Math.PI / 2, label: 'Пожежний щиток' },
  { id: 'bin_corridor_1', type: 'trashBin', x: -2.0, y: 0, z: -8.0, label: 'Смітник коридорний 1' },
  { id: 'bin_corridor_2', type: 'trashBin', x: 2.0, y: 0, z: -38.0, label: 'Смітник коридорний 2' },
  { id: 'debris_1', type: 'debris', x: -1.8, y: 0, z: -14.0, label: 'Розбитий посуд' },
  { id: 'debris_2', type: 'debris', x: 1.8, y: 0, z: -27.0, label: 'Уламки скла та плитки' },
  { id: 'tray_1', type: 'cableTray', x: 0, y: 0, z: -1.0, rotation: 0, label: 'Стельовий лоток 1' },
  { id: 'tray_2', type: 'cableTray', x: 0, y: 0, z: -16.0, rotation: 0, label: 'Стельовий лоток 2' },
  { id: 'tray_3', type: 'cableTray', x: 0, y: 0, z: -31.0, rotation: 0, label: 'Стельовий лоток 3' },
  { id: 'tray_4', type: 'cableTray', x: 0, y: 0, z: -46.0, rotation: 0, label: 'Стельовий лоток 4' },

  // Crates & Barrels (Indoor)
  { id: 'crate_1', type: 'crate', x: -10.5, y: 0, z: -41.5, size: 0.9, rotation: 0.1, label: 'Ящик 1' },
  { id: 'crate_2', type: 'crate', x: -10.5, y: 0, z: -42.7, size: 0.85, rotation: -0.15, label: 'Ящик 2' },
  { id: 'crate_3', type: 'crate', x: -10.5, y: 0.9, z: -42.1, size: 0.8, rotation: 0.05, label: 'Ящик 3' },
  { id: 'crate_4', type: 'crate', x: -4.2, y: 0, z: -52.0, size: 1.0, rotation: 0.2, label: 'Ящик 4' },
  { id: 'barrel_1', type: 'barrel', x: -3.8, y: 0, z: -41.5, color: '#3d4e42', rotation: 0, label: 'Бочка 1' },
  { id: 'barrel_2', type: 'barrel', x: -4.5, y: 0, z: -42.0, color: '#6e3d2c', rotation: 0, label: 'Бочка 2' },

  // Courtyard Atmospheric Props
  { id: 'guard_booth_1', type: 'guardBooth', x: -12.5, y: 0, z: 32.0, rotation: 0, label: 'КПП / Будка охорони' },
  { id: 'barrier_gate_1', type: 'barrier', x: -7.0, y: 0, z: 34.5, rotation: 0, label: 'Шлагбаум в’їзду' },
  { id: 'tower_nw', type: 'floodlightTower', x: -18.5, y: 0, z: 28.0, rotation: 0, label: 'Прожекторна вишка Пн-Зх' },
  { id: 'tower_ne', type: 'floodlightTower', x: 18.5, y: 0, z: 28.0, rotation: 0, label: 'Прожекторна вишка Пн-Сх' },
  { id: 'spool_1', type: 'cableSpool', x: 14.5, y: 0, z: 18.0, rotation: 0, label: 'Кабельна котушка 1' },
  { id: 'spool_2', type: 'cableSpool', x: 16.2, y: 0, z: 19.5, rotation: 0.5, label: 'Кабельна котушка 2' },
  { id: 'wreckage_truck', type: 'abandonedCar', x: 8.5, y: 0, z: 22.0, rotation: 0.42, label: 'Покинутий транспорт ГАЗ-66' },
  { id: 'barrel_yard_1', type: 'barrel', x: 16.1, y: 0, z: 15.0, color: '#6e3d2c', label: 'Бочка на подвір’ї 1' },
  { id: 'barrel_yard_2', type: 'barrel', x: 17.0, y: 0, z: 15.2, color: '#3d4e42', label: 'Бочка на подвір’ї 2' },
  { id: 'barrel_yard_3', type: 'barrel', x: 18.7, y: 0, z: 14.3, color: '#6e3d2c', label: 'Бочка на подвір’ї 3' },
  { id: 'crate_yard_1', type: 'crate', x: 17.7, y: 0, z: 17.0, size: 0.95, label: 'Ящик на подвір’ї 1' },
  { id: 'crate_yard_2', type: 'crate', x: 19.0, y: 0, z: 17.0, size: 0.95, label: 'Ящик на подвір’ї 2' },
  { id: 'crate_yard_3', type: 'crate', x: -14.5, y: 0, z: 31.0, size: 0.9, label: 'Ящик біля КПП' },
  { id: 'barrel_yard_4', type: 'barrel', x: -14.8, y: 0, z: 32.5, color: '#3d4e42', label: 'Бочка біля КПП' },
];

export const PROP_CATALOG = [
  // Core & Furniture (9)
  { type: 'table', name: 'Стіл / Верстак', icon: '🪵', defaultW: 2.2, defaultD: 1.2 },
  { type: 'chair', name: 'Стілець оператора', icon: '🪑' },
  { type: 'locker', name: 'Шафа (Сховок)', icon: '🚪' },
  { type: 'shelf', name: 'Стелаж архіву', icon: '🗄' },
  { type: 'serverRack', name: 'Серверна стійка', icon: '🖧' },
  { type: 'crate', name: 'Дерев’яний ящик', icon: '📦', defaultSize: 0.9 },
  { type: 'barrel', name: 'Металева бочка', icon: '🛢' },
  { type: 'crt', name: 'Монітор ЕЛП', icon: '🖥' },
  { type: 'camera', name: 'Камера ТК–04', icon: '📹' },

  // Courtyard (5)
  { type: 'guardBooth', name: 'КПП / Будка охорони', icon: '🏠', defaultW: 2.4, defaultD: 2.2 },
  { type: 'barrier', name: 'Шлагбаум зі стійкою', icon: '🚧', defaultW: 3.0, defaultD: 0.4 },
  { type: 'floodlightTower', name: 'Прожекторна вишка', icon: '🗼', defaultW: 1.2, defaultD: 1.2 },
  { type: 'cableSpool', name: 'Кабельна котушка', icon: '🪵', defaultW: 1.2, defaultD: 1.2 },
  { type: 'abandonedCar', name: 'Покинутий транспорт', icon: '🚗', defaultW: 2.1, defaultD: 4.2 },

  // Hall & Corridor (7)
  { type: 'turnstile', name: 'Турнікети входу', icon: '🛑', defaultW: 2.8, defaultD: 0.8 },
  { type: 'honorBoard', name: 'Дошка пошани', icon: '🎖', defaultW: 2.2, defaultD: 0.1 },
  { type: 'scheduleBoard', name: 'Сітка мовлення', icon: '📋', defaultW: 2.4, defaultD: 0.1 },
  { type: 'fireBoard', name: 'Пожежний щиток', icon: '🧯', defaultW: 1.3, defaultD: 0.25 },
  { type: 'cableTray', name: 'Стельовий лоток', icon: '🪜', defaultW: 0.5, defaultD: 3.0 },
  { type: 'trashBin', name: 'Смітник коридорний', icon: '🗑', defaultW: 0.5, defaultD: 0.5 },
  { type: 'debris', name: 'Уламки та сміття', icon: '🪨', defaultW: 1.0, defaultD: 1.0 },

  // Archive & Studio (7)
  { type: 'cardCatalog', name: 'Каталожна шафа', icon: '🗃', defaultW: 1.6, defaultD: 0.7 },
  { type: 'archivistDesk', name: 'Стіл з лампою', icon: '💼', defaultW: 2.2, defaultD: 1.2 },
  { type: 'vhsStack', name: 'Стопка касет Betacam', icon: '📼', defaultW: 0.75, defaultD: 0.55 },
  { type: 'anchorDesk', name: 'Дикторський стіл', icon: '🎙', defaultW: 3.4, defaultD: 1.5 },
  { type: 'chromakeyBackdrop', name: 'Хромакей / Куліси', icon: '🟩', defaultW: 4.6, defaultD: 0.3 },
  { type: 'micStand', name: 'Мікрофонна стійка', icon: '🎤', defaultW: 0.4, defaultD: 0.4 },
  { type: 'ceilingSpotlight', name: 'Освітлювальний софіт', icon: '💡', defaultW: 0.5, defaultD: 0.5 },

  // Generator & Vent & Server (7)
  { type: 'dieselGenerator', name: 'Дизель-генератор', icon: '⚡', defaultW: 3.2, defaultD: 1.8 },
  { type: 'fuelCanister', name: 'Каністра солярки', icon: '🛢', defaultW: 0.5, defaultD: 0.35 },
  { type: 'fuelValve', name: 'Аварійний вентиль', icon: '🔴', defaultW: 0.6, defaultD: 0.5 },
  { type: 'fusePanel', name: 'Щит 380В з гніздами', icon: '🔌', defaultW: 1.1, defaultD: 0.35 },
  { type: 'airDuct', name: 'Вентиляційний короб', icon: '🏭', defaultW: 1.1, defaultD: 1.1 },
  { type: 'ventGrate', name: 'Вентиляційна решітка', icon: '▦', defaultW: 1.2, defaultD: 0.1 },
  { type: 'cableBundle', name: 'Кабельні джгути', icon: '〰', defaultW: 2.0, defaultD: 0.8 },

  // Apparatus & Control (4)
  { type: 'mixingConsole', name: 'Пульт прямого ефіру', icon: '🎛', defaultW: 2.6, defaultD: 1.4 },
  { type: 'oscilloscope', name: 'Осцилограф', icon: '📈', defaultW: 0.5, defaultD: 0.5 },
  { type: 'monitorWall', name: 'Стійка відеоконтролю', icon: '📺', defaultW: 2.2, defaultD: 0.7 },
  { type: 'acousticPanel', name: 'Акустична панель', icon: '🧱', defaultW: 1.2, defaultD: 0.1 },
];
