CREATE TABLE IF NOT EXISTS robotmessage (id INT NOT NULL PRIMARY KEY, clampname VARCHAR(50) DEFAULT '', clampstatus INT DEFAULT 0, clampposstatus INT DEFAULT 0, workpiecename VARCHAR(100) DEFAULT '', workpiecestatus INT DEFAULT 0) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
INSERT INTO robotmessage (id) VALUES (1) ON DUPLICATE KEY UPDATE id=id;
SELECT * FROM robotmessage;
