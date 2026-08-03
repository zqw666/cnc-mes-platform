import fitz
import sys
sys.stdout.reconfigure(encoding='utf-8')
pdf_path = r"C:\Users\zqw\AppData\Local\Temp\codebuddy-dropped-files\4478c86f-fd56-415d-ae19-eb253e65f448\828D_840Dsl_手册_SINUMERIK OPC UA 3.1 配置及样例使用说明_2022.pdf"
print("PDF path exists:", __import__('os').path.exists(pdf_path))
doc = fitz.open(pdf_path)
print("Pages:", doc.page_count)
for i in range(min(doc.page_count, 25)):
    text = doc[i].get_text()
    if text.strip():
        print(f"=== Page {i+1} ===")
        print(text[:2000])
