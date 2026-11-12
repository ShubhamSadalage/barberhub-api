-- ============================================================================
-- BarberHub 100% Bulletproof Test Data Seeding & Password Repair Script
-- Database: PostgreSQL (barberhub)
-- Fixes corrupted password hashes with verified ASP.NET Identity V3 PBKDF2 hashes
-- ============================================================================

DO $$
DECLARE
    v_user_id text;
    v_barber_id text;
    v_role_user text := 'role-user';
    v_role_admin text := 'role-admin';
    v_role_superadmin text := 'role-superadmin';
    -- Valid ASP.NET Core Identity V3 PBKDF2 Hashes
    v_admin_pwd_hash text := 'AQAAAAIAAYagAAAAEOt4KPRYCM+rQf29TzP/lSnbLHX7FR7BIQzHL9MF5KKhpsLHISVzhK0ro/ViQ4g39g==';   -- Admin@123
    v_user_pwd_hash text := 'AQAAAAIAAYagAAAAEFYU3SAUcZPBPAja/yDZojeXUYiZyOtcktPYz/nuhB+zuWXDXhpc6YNqN1ezM6YG1Q==';    -- User@123
    v_super_pwd_hash text := 'AQAAAAIAAYagAAAAEEV2eAaw4hRapf6JKZFXPF2An8sVKLPBmXmOgpC6GN3VFLvqgwUwImbO93grDEAm5w==';   -- SuperAdmin@123
BEGIN

    -- ------------------------------------------------------------------------
    -- 1. Ensure Roles Exist
    -- ------------------------------------------------------------------------
    INSERT INTO "AspNetRoles" ("Id", "Name", "NormalizedName", "ConcurrencyStamp")
    VALUES 
        (v_role_user, 'User', 'USER', gen_random_uuid()::text),
        (v_role_admin, 'Admin', 'ADMIN', gen_random_uuid()::text),
        (v_role_superadmin, 'SuperAdmin', 'SUPERADMIN', gen_random_uuid()::text)
    ON CONFLICT ("Id") DO NOTHING;

    -- ------------------------------------------------------------------------
    -- 2. BARBER 1: John's Barber Shop (barber@barberhub.com / Admin@123)
    -- ------------------------------------------------------------------------
    SELECT "Id" INTO v_barber_id FROM "AspNetUsers" 
    WHERE "NormalizedEmail" = 'BARBER@BARBERHUB.COM' OR "NormalizedUserName" = 'BARBER@BARBERHUB.COM' OR "Id" = 'user-barber-001'
    LIMIT 1;

    IF v_barber_id IS NULL THEN
        v_barber_id := 'user-barber-001';
        BEGIN
            INSERT INTO "AspNetUsers" (
                "Id", "FullName", "UserName", "NormalizedUserName", "Email", "NormalizedEmail",
                "EmailConfirmed", "PhoneNumber", "PhoneNumberConfirmed", "TwoFactorEnabled",
                "LockoutEnabled", "AccessFailedCount", "IsActive", "IsDeleted", "CreatedAt",
                "ShopName", "ShopDescription", "City", "Address", "Latitude", "Longitude",
                "WeeklyHoliday", "OpeningTime", "ClosingTime", "ApprovalStatus", "ApprovedAt",
                "PasswordHash", "SecurityStamp", "ConcurrencyStamp"
            ) VALUES (
                v_barber_id, 'John Doe', 'barber@barberhub.com', 'BARBER@BARBERHUB.COM', 'barber@barberhub.com', 'BARBER@BARBERHUB.COM',
                TRUE, '9999999999', TRUE, FALSE, TRUE, 0, TRUE, FALSE, NOW(),
                'John''s Premium Barber Shop', 'Premium grooming for modern gentlemen. 15+ years of experience.',
                'Pune', '123 MG Road, Camp, Pune - 411001', 18.5204, 73.8567, 1, '09:00:00'::time, '20:00:00'::time, 2, NOW(),
                v_admin_pwd_hash, gen_random_uuid()::text, gen_random_uuid()::text
            );
        EXCEPTION WHEN unique_violation THEN
            SELECT "Id" INTO v_barber_id FROM "AspNetUsers" WHERE "NormalizedEmail" = 'BARBER@BARBERHUB.COM' LIMIT 1;
        END;
    END IF;

    UPDATE "AspNetUsers" 
    SET "ApprovalStatus" = 2, "IsActive" = TRUE, "PasswordHash" = v_admin_pwd_hash,
        "ShopName" = COALESCE("ShopName", 'John''s Premium Barber Shop'),
        "OpeningTime" = COALESCE("OpeningTime", '09:00:00'::time),
        "ClosingTime" = COALESCE("ClosingTime", '20:00:00'::time)
    WHERE "Id" = v_barber_id;

    INSERT INTO "AspNetUserRoles" ("UserId", "RoleId") VALUES (v_barber_id, v_role_admin) ON CONFLICT DO NOTHING;

    -- Barber 1 Services
    INSERT INTO "Services" ("Id", "Name", "Description", "Category", "Price", "DurationMinutes", "IsActive", "BarberId", "CreatedAt")
    VALUES 
        ('a0000001-0000-0000-0000-000000000001', 'Haircut', 'Classic men''s haircut with styling', 'Hair', 300.00, 30, TRUE, v_barber_id, NOW()),
        ('a0000001-0000-0000-0000-000000000002', 'Beard Trim', 'Professional beard trimming and shaping', 'Beard', 200.00, 20, TRUE, v_barber_id, NOW()),
        ('a0000001-0000-0000-0000-000000000003', 'Haircut + Beard', 'Complete grooming package', 'Package', 450.00, 45, TRUE, v_barber_id, NOW()),
        ('a0000001-0000-0000-0000-000000000004', 'Hair Color', 'Hair coloring service', 'Color', 1200.00, 90, TRUE, v_barber_id, NOW()),
        ('a0000001-0000-0000-0000-000000000005', 'Head Massage', 'Relaxing head and shoulder massage', 'Massage', 400.00, 30, TRUE, v_barber_id, NOW())
    ON CONFLICT ("Id") DO NOTHING;


    -- ------------------------------------------------------------------------
    -- 3. BARBER 2: Alex's Grooming Lounge (alex@barberhub.com / Admin@123)
    -- ------------------------------------------------------------------------
    SELECT "Id" INTO v_barber_id FROM "AspNetUsers" 
    WHERE "NormalizedEmail" = 'ALEX@BARBERHUB.COM' OR "NormalizedUserName" = 'ALEX@BARBERHUB.COM' OR "Id" = 'user-barber-002'
    LIMIT 1;

    IF v_barber_id IS NULL THEN
        v_barber_id := 'user-barber-002';
        BEGIN
            INSERT INTO "AspNetUsers" (
                "Id", "FullName", "UserName", "NormalizedUserName", "Email", "NormalizedEmail",
                "EmailConfirmed", "PhoneNumber", "PhoneNumberConfirmed", "TwoFactorEnabled",
                "LockoutEnabled", "AccessFailedCount", "IsActive", "IsDeleted", "CreatedAt",
                "ShopName", "ShopDescription", "City", "Address", "Latitude", "Longitude",
                "WeeklyHoliday", "OpeningTime", "ClosingTime", "ApprovalStatus", "ApprovedAt",
                "PasswordHash", "SecurityStamp", "ConcurrencyStamp"
            ) VALUES (
                v_barber_id, 'Alex Morgan', 'alex@barberhub.com', 'ALEX@BARBERHUB.COM', 'alex@barberhub.com', 'ALEX@BARBERHUB.COM',
                TRUE, '9988776655', TRUE, FALSE, TRUE, 0, TRUE, FALSE, NOW(),
                'Alex''s Grooming Lounge', 'Specialized in fade cuts, beard styling, and hot towel shaves.',
                'Pune', 'Shop 4, North Main Road, Koregaon Park, Pune - 411001', 18.5362, 73.8940, 2, '10:00:00'::time, '21:00:00'::time, 2, NOW(),
                v_admin_pwd_hash, gen_random_uuid()::text, gen_random_uuid()::text
            );
        EXCEPTION WHEN unique_violation THEN
            SELECT "Id" INTO v_barber_id FROM "AspNetUsers" WHERE "NormalizedEmail" = 'ALEX@BARBERHUB.COM' LIMIT 1;
        END;
    END IF;

    UPDATE "AspNetUsers" 
    SET "ApprovalStatus" = 2, "IsActive" = TRUE, "PasswordHash" = v_admin_pwd_hash,
        "ShopName" = COALESCE("ShopName", 'Alex''s Grooming Lounge'),
        "OpeningTime" = COALESCE("OpeningTime", '10:00:00'::time),
        "ClosingTime" = COALESCE("ClosingTime", '21:00:00'::time)
    WHERE "Id" = v_barber_id;

    INSERT INTO "AspNetUserRoles" ("UserId", "RoleId") VALUES (v_barber_id, v_role_admin) ON CONFLICT DO NOTHING;

    -- Barber 2 Services
    INSERT INTO "Services" ("Id", "Name", "Description", "Category", "Price", "DurationMinutes", "IsActive", "BarberId", "CreatedAt")
    VALUES 
        ('a0000002-0000-0000-0000-000000000001', 'Skin Fade & Style', 'Precision low/mid/high skin fade', 'Hair', 400.00, 30, TRUE, v_barber_id, NOW()),
        ('a0000002-0000-0000-0000-000000000002', 'Hot Towel Shave', 'Luxury straight-razor shave with hot towel', 'Beard', 350.00, 30, TRUE, v_barber_id, NOW()),
        ('a0000002-0000-0000-0000-000000000003', 'VIP Beard Sculpting', 'Beard contouring with organic oils', 'Beard', 300.00, 25, TRUE, v_barber_id, NOW()),
        ('a0000002-0000-0000-0000-000000000004', 'Charcoal Face Detox', 'Pore cleansing and blackhead removal', 'Facial', 600.00, 35, TRUE, v_barber_id, NOW())
    ON CONFLICT ("Id") DO NOTHING;


    -- ------------------------------------------------------------------------
    -- 4. BARBER 3: Royal Fade Barbershop (royalfade@barberhub.com / Admin@123)
    -- ------------------------------------------------------------------------
    SELECT "Id" INTO v_barber_id FROM "AspNetUsers" 
    WHERE "NormalizedEmail" = 'ROYALFADE@BARBERHUB.COM' OR "NormalizedUserName" = 'ROYALFADE@BARBERHUB.COM' OR "Id" = 'user-barber-003'
    LIMIT 1;

    IF v_barber_id IS NULL THEN
        v_barber_id := 'user-barber-003';
        BEGIN
            INSERT INTO "AspNetUsers" (
                "Id", "FullName", "UserName", "NormalizedUserName", "Email", "NormalizedEmail",
                "EmailConfirmed", "PhoneNumber", "PhoneNumberConfirmed", "TwoFactorEnabled",
                "LockoutEnabled", "AccessFailedCount", "IsActive", "IsDeleted", "CreatedAt",
                "ShopName", "ShopDescription", "City", "Address", "Latitude", "Longitude",
                "WeeklyHoliday", "OpeningTime", "ClosingTime", "ApprovalStatus", "ApprovedAt",
                "PasswordHash", "SecurityStamp", "ConcurrencyStamp"
            ) VALUES (
                v_barber_id, 'Vikram Malhotra', 'royalfade@barberhub.com', 'ROYALFADE@BARBERHUB.COM', 'royalfade@barberhub.com', 'ROYALFADE@BARBERHUB.COM',
                TRUE, '9977665544', TRUE, FALSE, TRUE, 0, TRUE, FALSE, NOW(),
                'Royal Fade Barbershop', 'Modern hair designs, hair treatments, and VIP grooming packages.',
                'Pune', 'Plot 12, Paud Road, Kothrud, Pune - 411038', 18.5074, 73.8077, 3, '09:30:00'::time, '20:30:00'::time, 2, NOW(),
                v_admin_pwd_hash, gen_random_uuid()::text, gen_random_uuid()::text
            );
        EXCEPTION WHEN unique_violation THEN
            SELECT "Id" INTO v_barber_id FROM "AspNetUsers" WHERE "NormalizedEmail" = 'ROYALFADE@BARBERHUB.COM' LIMIT 1;
        END;
    END IF;

    UPDATE "AspNetUsers" 
    SET "ApprovalStatus" = 2, "IsActive" = TRUE, "PasswordHash" = v_admin_pwd_hash,
        "ShopName" = COALESCE("ShopName", 'Royal Fade Barbershop'),
        "OpeningTime" = COALESCE("OpeningTime", '09:30:00'::time),
        "ClosingTime" = COALESCE("ClosingTime", '20:30:00'::time)
    WHERE "Id" = v_barber_id;

    INSERT INTO "AspNetUserRoles" ("UserId", "RoleId") VALUES (v_barber_id, v_role_admin) ON CONFLICT DO NOTHING;

    -- Barber 3 Services
    INSERT INTO "Services" ("Id", "Name", "Description", "Category", "Price", "DurationMinutes", "IsActive", "BarberId", "CreatedAt")
    VALUES 
        ('a0000003-0000-0000-0000-000000000001', 'Royal Signature Haircut', 'Haircut, hair wash & tonic treatment', 'Hair', 500.00, 40, TRUE, v_barber_id, NOW()),
        ('a0000003-0000-0000-0000-000000000002', 'Beard Spa & Trim', 'Steam conditioning and trimming', 'Beard', 350.00, 30, TRUE, v_barber_id, NOW()),
        ('a0000003-0000-0000-0000-000000000003', 'Keratin Hair Spa', 'Deep moisturizing keratin therapy', 'Hair Care', 1500.00, 60, TRUE, v_barber_id, NOW())
    ON CONFLICT ("Id") DO NOTHING;


    -- ------------------------------------------------------------------------
    -- 5. BARBER 4: Blade & Brush Salon (bladebrush@barberhub.com / Admin@123)
    -- ------------------------------------------------------------------------
    SELECT "Id" INTO v_barber_id FROM "AspNetUsers" 
    WHERE "NormalizedEmail" = 'BLADEBRUSH@BARBERHUB.COM' OR "NormalizedUserName" = 'BLADEBRUSH@BARBERHUB.COM' OR "Id" = 'user-barber-004'
    LIMIT 1;

    IF v_barber_id IS NULL THEN
        v_barber_id := 'user-barber-004';
        BEGIN
            INSERT INTO "AspNetUsers" (
                "Id", "FullName", "UserName", "NormalizedUserName", "Email", "NormalizedEmail",
                "EmailConfirmed", "PhoneNumber", "PhoneNumberConfirmed", "TwoFactorEnabled",
                "LockoutEnabled", "AccessFailedCount", "IsActive", "IsDeleted", "CreatedAt",
                "ShopName", "ShopDescription", "City", "Address", "Latitude", "Longitude",
                "WeeklyHoliday", "OpeningTime", "ClosingTime", "ApprovalStatus", "ApprovedAt",
                "PasswordHash", "SecurityStamp", "ConcurrencyStamp"
            ) VALUES (
                v_barber_id, 'Siddharth Rao', 'bladebrush@barberhub.com', 'BLADEBRUSH@BARBERHUB.COM', 'bladebrush@barberhub.com', 'BLADEBRUSH@BARBERHUB.COM',
                TRUE, '9966554433', TRUE, FALSE, TRUE, 0, TRUE, FALSE, NOW(),
                'Blade & Brush Salon', 'High-end luxury salon & spa for modern styling and relaxation.',
                'Pune', 'Phoenix Marketcity Area, Viman Nagar, Pune - 411014', 18.5679, 73.9143, 1, '10:00:00'::time, '22:00:00'::time, 2, NOW(),
                v_admin_pwd_hash, gen_random_uuid()::text, gen_random_uuid()::text
            );
        EXCEPTION WHEN unique_violation THEN
            SELECT "Id" INTO v_barber_id FROM "AspNetUsers" WHERE "NormalizedEmail" = 'BLADEBRUSH@BARBERHUB.COM' LIMIT 1;
        END;
    END IF;

    UPDATE "AspNetUsers" 
    SET "ApprovalStatus" = 2, "IsActive" = TRUE, "PasswordHash" = v_admin_pwd_hash,
        "ShopName" = COALESCE("ShopName", 'Blade & Brush Salon'),
        "OpeningTime" = COALESCE("OpeningTime", '10:00:00'::time),
        "ClosingTime" = COALESCE("ClosingTime", '22:00:00'::time)
    WHERE "Id" = v_barber_id;

    INSERT INTO "AspNetUserRoles" ("UserId", "RoleId") VALUES (v_barber_id, v_role_admin) ON CONFLICT DO NOTHING;

    -- Barber 4 Services
    INSERT INTO "Services" ("Id", "Name", "Description", "Category", "Price", "DurationMinutes", "IsActive", "BarberId", "CreatedAt")
    VALUES 
        ('a0000004-0000-0000-0000-000000000001', 'Executive Haircut', 'Stylist consultation, wash and finish', 'Hair', 600.00, 45, TRUE, v_barber_id, NOW()),
        ('a0000004-0000-0000-0000-000000000002', 'Beard Styling & Lineup', 'Razor lineup and mustache styling', 'Beard', 300.00, 20, TRUE, v_barber_id, NOW()),
        ('a0000004-0000-0000-0000-000000000003', 'Gold Glow Facial', 'Luxury brightening and rejuvenating facial', 'Facial', 1200.00, 45, TRUE, v_barber_id, NOW())
    ON CONFLICT ("Id") DO NOTHING;


    -- ------------------------------------------------------------------------
    -- 6. CUSTOMERS (All with Password: User@123)
    -- ------------------------------------------------------------------------

    -- Customer 0: user@barberhub.com
    SELECT "Id" INTO v_user_id FROM "AspNetUsers" 
    WHERE "NormalizedEmail" = 'USER@BARBERHUB.COM' OR "NormalizedUserName" = 'USER@BARBERHUB.COM' OR "Id" = 'user-customer-001' LIMIT 1;
    IF v_user_id IS NULL THEN
        v_user_id := 'user-customer-001';
        BEGIN
            INSERT INTO "AspNetUsers" (
                "Id", "FullName", "UserName", "NormalizedUserName", "Email", "NormalizedEmail",
                "EmailConfirmed", "PhoneNumber", "PhoneNumberConfirmed", "TwoFactorEnabled",
                "LockoutEnabled", "AccessFailedCount", "IsActive", "IsDeleted", "CreatedAt",
                "City", "Latitude", "Longitude", "ApprovalStatus",
                "PasswordHash", "SecurityStamp", "ConcurrencyStamp"
            ) VALUES (
                v_user_id, 'Demo User', 'user@barberhub.com', 'USER@BARBERHUB.COM', 'user@barberhub.com', 'USER@BARBERHUB.COM',
                TRUE, '8888888888', TRUE, FALSE, TRUE, 0, TRUE, FALSE, NOW(), 'Pune', 18.5304, 73.8467, 0,
                v_user_pwd_hash, gen_random_uuid()::text, gen_random_uuid()::text
            );
        EXCEPTION WHEN unique_violation THEN
            SELECT "Id" INTO v_user_id FROM "AspNetUsers" WHERE "NormalizedEmail" = 'USER@BARBERHUB.COM' LIMIT 1;
        END;
    END IF;
    UPDATE "AspNetUsers" SET "IsActive" = TRUE, "PasswordHash" = v_user_pwd_hash WHERE "Id" = v_user_id;
    INSERT INTO "AspNetUserRoles" ("UserId", "RoleId") VALUES (v_user_id, v_role_user) ON CONFLICT DO NOTHING;

    -- Customer 1: user1@barberhub.com
    SELECT "Id" INTO v_user_id FROM "AspNetUsers" 
    WHERE "NormalizedEmail" = 'USER1@BARBERHUB.COM' OR "NormalizedUserName" = 'USER1@BARBERHUB.COM' OR "Id" = 'user-customer-002' LIMIT 1;
    IF v_user_id IS NULL THEN
        v_user_id := 'user-customer-002';
        BEGIN
            INSERT INTO "AspNetUsers" (
                "Id", "FullName", "UserName", "NormalizedUserName", "Email", "NormalizedEmail",
                "EmailConfirmed", "PhoneNumber", "PhoneNumberConfirmed", "TwoFactorEnabled",
                "LockoutEnabled", "AccessFailedCount", "IsActive", "IsDeleted", "CreatedAt",
                "City", "Latitude", "Longitude", "ApprovalStatus",
                "PasswordHash", "SecurityStamp", "ConcurrencyStamp"
            ) VALUES (
                v_user_id, 'Rahul Sharma', 'user1@barberhub.com', 'USER1@BARBERHUB.COM', 'user1@barberhub.com', 'USER1@BARBERHUB.COM',
                TRUE, '9820011111', TRUE, FALSE, TRUE, 0, TRUE, FALSE, NOW(), 'Pune', 18.5200, 73.8500, 0,
                v_user_pwd_hash, gen_random_uuid()::text, gen_random_uuid()::text
            );
        EXCEPTION WHEN unique_violation THEN
            SELECT "Id" INTO v_user_id FROM "AspNetUsers" WHERE "NormalizedEmail" = 'USER1@BARBERHUB.COM' LIMIT 1;
        END;
    END IF;
    UPDATE "AspNetUsers" SET "IsActive" = TRUE, "PasswordHash" = v_user_pwd_hash WHERE "Id" = v_user_id;
    INSERT INTO "AspNetUserRoles" ("UserId", "RoleId") VALUES (v_user_id, v_role_user) ON CONFLICT DO NOTHING;

    -- Customer 2: user2@barberhub.com
    SELECT "Id" INTO v_user_id FROM "AspNetUsers" 
    WHERE "NormalizedEmail" = 'USER2@BARBERHUB.COM' OR "NormalizedUserName" = 'USER2@BARBERHUB.COM' OR "Id" = 'user-customer-003' LIMIT 1;
    IF v_user_id IS NULL THEN
        v_user_id := 'user-customer-003';
        BEGIN
            INSERT INTO "AspNetUsers" (
                "Id", "FullName", "UserName", "NormalizedUserName", "Email", "NormalizedEmail",
                "EmailConfirmed", "PhoneNumber", "PhoneNumberConfirmed", "TwoFactorEnabled",
                "LockoutEnabled", "AccessFailedCount", "IsActive", "IsDeleted", "CreatedAt",
                "City", "Latitude", "Longitude", "ApprovalStatus",
                "PasswordHash", "SecurityStamp", "ConcurrencyStamp"
            ) VALUES (
                v_user_id, 'Sneha Patil', 'user2@barberhub.com', 'USER2@BARBERHUB.COM', 'user2@barberhub.com', 'USER2@BARBERHUB.COM',
                TRUE, '9820022222', TRUE, FALSE, TRUE, 0, TRUE, FALSE, NOW(), 'Pune', 18.5350, 73.8900, 0,
                v_user_pwd_hash, gen_random_uuid()::text, gen_random_uuid()::text
            );
        EXCEPTION WHEN unique_violation THEN
            SELECT "Id" INTO v_user_id FROM "AspNetUsers" WHERE "NormalizedEmail" = 'USER2@BARBERHUB.COM' LIMIT 1;
        END;
    END IF;
    UPDATE "AspNetUsers" SET "IsActive" = TRUE, "PasswordHash" = v_user_pwd_hash WHERE "Id" = v_user_id;
    INSERT INTO "AspNetUserRoles" ("UserId", "RoleId") VALUES (v_user_id, v_role_user) ON CONFLICT DO NOTHING;

    -- Customer 3: user3@barberhub.com
    SELECT "Id" INTO v_user_id FROM "AspNetUsers" 
    WHERE "NormalizedEmail" = 'USER3@BARBERHUB.COM' OR "NormalizedUserName" = 'USER3@BARBERHUB.COM' OR "Id" = 'user-customer-004' LIMIT 1;
    IF v_user_id IS NULL THEN
        v_user_id := 'user-customer-004';
        BEGIN
            INSERT INTO "AspNetUsers" (
                "Id", "FullName", "UserName", "NormalizedUserName", "Email", "NormalizedEmail",
                "EmailConfirmed", "PhoneNumber", "PhoneNumberConfirmed", "TwoFactorEnabled",
                "LockoutEnabled", "AccessFailedCount", "IsActive", "IsDeleted", "CreatedAt",
                "City", "Latitude", "Longitude", "ApprovalStatus",
                "PasswordHash", "SecurityStamp", "ConcurrencyStamp"
            ) VALUES (
                v_user_id, 'Amit Verma', 'user3@barberhub.com', 'USER3@BARBERHUB.COM', 'user3@barberhub.com', 'USER3@BARBERHUB.COM',
                TRUE, '9820033333', TRUE, FALSE, TRUE, 0, TRUE, FALSE, NOW(), 'Pune', 18.5100, 73.8100, 0,
                v_user_pwd_hash, gen_random_uuid()::text, gen_random_uuid()::text
            );
        EXCEPTION WHEN unique_violation THEN
            SELECT "Id" INTO v_user_id FROM "AspNetUsers" WHERE "NormalizedEmail" = 'USER3@BARBERHUB.COM' LIMIT 1;
        END;
    END IF;
    UPDATE "AspNetUsers" SET "IsActive" = TRUE, "PasswordHash" = v_user_pwd_hash WHERE "Id" = v_user_id;
    INSERT INTO "AspNetUserRoles" ("UserId", "RoleId") VALUES (v_user_id, v_role_user) ON CONFLICT DO NOTHING;

    -- Customer 4: user4@barberhub.com
    SELECT "Id" INTO v_user_id FROM "AspNetUsers" 
    WHERE "NormalizedEmail" = 'USER4@BARBERHUB.COM' OR "NormalizedUserName" = 'USER4@BARBERHUB.COM' OR "Id" = 'user-customer-005' LIMIT 1;
    IF v_user_id IS NULL THEN
        v_user_id := 'user-customer-005';
        BEGIN
            INSERT INTO "AspNetUsers" (
                "Id", "FullName", "UserName", "NormalizedUserName", "Email", "NormalizedEmail",
                "EmailConfirmed", "PhoneNumber", "PhoneNumberConfirmed", "TwoFactorEnabled",
                "LockoutEnabled", "AccessFailedCount", "IsActive", "IsDeleted", "CreatedAt",
                "City", "Latitude", "Longitude", "ApprovalStatus",
                "PasswordHash", "SecurityStamp", "ConcurrencyStamp"
            ) VALUES (
                v_user_id, 'Priya Deshmukh', 'user4@barberhub.com', 'USER4@BARBERHUB.COM', 'user4@barberhub.com', 'USER4@BARBERHUB.COM',
                TRUE, '9820044444', TRUE, FALSE, TRUE, 0, TRUE, FALSE, NOW(), 'Pune', 18.5600, 73.9100, 0,
                v_user_pwd_hash, gen_random_uuid()::text, gen_random_uuid()::text
            );
        EXCEPTION WHEN unique_violation THEN
            SELECT "Id" INTO v_user_id FROM "AspNetUsers" WHERE "NormalizedEmail" = 'USER4@BARBERHUB.COM' LIMIT 1;
        END;
    END IF;
    UPDATE "AspNetUsers" SET "IsActive" = TRUE, "PasswordHash" = v_user_pwd_hash WHERE "Id" = v_user_id;
    INSERT INTO "AspNetUserRoles" ("UserId", "RoleId") VALUES (v_user_id, v_role_user) ON CONFLICT DO NOTHING;

    -- ------------------------------------------------------------------------
    -- 7. SUPERADMIN (superadmin@barberhub.com / SuperAdmin@123)
    -- ------------------------------------------------------------------------
    SELECT "Id" INTO v_user_id FROM "AspNetUsers" 
    WHERE "NormalizedEmail" = 'SUPERADMIN@BARBERHUB.COM' OR "NormalizedUserName" = 'SUPERADMIN@BARBERHUB.COM' LIMIT 1;
    IF v_user_id IS NOT NULL THEN
        UPDATE "AspNetUsers" SET "IsActive" = TRUE, "PasswordHash" = v_super_pwd_hash WHERE "Id" = v_user_id;
        INSERT INTO "AspNetUserRoles" ("UserId", "RoleId") VALUES (v_user_id, v_role_superadmin) ON CONFLICT DO NOTHING;
    END IF;

    RAISE NOTICE 'BarberHub test data passwords repaired and synchronized successfully!';
END $$;
