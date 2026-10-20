import html
import os

md_path = os.path.join("plans", "260924-2044-hms-server-management", "plan.md")
html_path = os.path.join("plans", "260924-2044-hms-server-management", "plan.html")

with open(md_path, "r", encoding="utf-8") as f:
    content = f.read()

html_content = f"""<!DOCTYPE html>
<html lang="vi">
<head>
  <meta charset="UTF-8" />
  <meta name="viewport" content="width=device-width, initial-scale=1.0" />
  <title>HMS-SERVER Management & Control Plane - Execution Plan</title>
  <style>
    body {{
      font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif;
      background: #0f172a;
      color: #f8fafc;
      line-height: 1.6;
      margin: 0;
      padding: 2rem;
    }}
    .container {{
      max-width: 1000px;
      margin: 0 auto;
      background: #1e293b;
      border: 1px solid #334155;
      border-radius: 12px;
      padding: 2.5rem;
      box-shadow: 0 10px 25px -5px rgba(0, 0, 0, 0.5);
    }}
    h1 {{ color: #38bdf8; font-size: 2rem; border-bottom: 2px solid #38bdf8; padding-bottom: 0.5rem; }}
    pre {{
      background: #090d16;
      border: 1px solid #334155;
      border-radius: 8px;
      padding: 1.5rem;
      overflow-x: auto;
      color: #e2e8f0;
      font-size: 0.9rem;
      white-space: pre-wrap;
      word-break: break-word;
    }}
    .badge {{
      display: inline-block;
      padding: 0.25rem 0.6rem;
      border-radius: 9999px;
      font-size: 0.75rem;
      font-weight: 600;
      background: rgba(74, 222, 128, 0.2);
      color: #4ade80;
      border: 1px solid #4ade80;
    }}
  </style>
</head>
<body>
  <div class="container">
    <div style="display:flex; justify-content:space-between; align-items:center; margin-bottom:1.5rem;">
      <h1>HMS-SERVER Execution Plan</h1>
      <span class="badge">VERIFIED PLAN</span>
    </div>
    <pre>{html.escape(content)}</pre>
  </div>
</body>
</html>"""

with open(html_path, "w", encoding="utf-8") as f:
    f.write(html_content)

print(f"Created {html_path} ({os.path.getsize(html_path)} bytes)")
