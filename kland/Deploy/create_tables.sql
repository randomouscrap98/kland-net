-- Wherever you put the database, make sure your appsettings.json points to it.

create table if not exists bans (
    range text unique,
    created text not null,
    note text
);

create table if not exists threads (
    tid integer primary key,
    created text not null,
    subject text not null,
    deleted int not null,
    hash text
);

create table if not exists posts (
    pid integer primary key,
    tid integer not null,
    created text not null,
    content text not null,
    options text not null,
    ipaddress text not null,
    username text,
    tripraw text,
    image text
);

create index if not exists idx_posts_tid on posts(tid);
