# Charity Run Backend (.NET 10)

A lightweight backend starter for a UI programming course project.

## Features

- Public charity and race information
- Runner registration and login
- Private runner dashboard APIs
- Teammate lookup by team name
- Admin runner management
- Email reminder preview endpoint
- JSON file persistence
- No Swagger or extra packages

## Run

```bash
dotnet run
```

Open:

- `/api-doc` for endpoint documentation
- `/test` for a simple browser test page

## Default Accounts

- `admin@example.com` / `Admin123!`
- `runner@example.com` / `Runner123!`

## Auth Header

```http
Authorization: Bearer YOUR_TOKEN
```

## Data File

Runner data is stored in `runners.json`.
