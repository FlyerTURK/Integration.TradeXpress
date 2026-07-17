import os
import re
import codecs

project_dir = r'E:\Kodlarim\Yeni\src'

def replace_in_file(filepath):
    with codecs.open(filepath, 'r', 'utf-8') as f:
        content = f.read()
    
    new_content = content
    # Replace the keys in C# and Razor files
    new_content = re.sub(r'Enum:ProcessDirectionType:Inbound:UI', 'UI:ProcessDirectionType:Inbound', new_content)
    new_content = re.sub(r'Enum:ProcessDirectionType:Outbound:UI', 'UI:ProcessDirectionType:Outbound', new_content)
    new_content = re.sub(r'Enum:ProcessPaymentType:Normal:UI', 'UI:ProcessPaymentType:Normal', new_content)
    new_content = re.sub(r'Enum:ProcessPaymentType:WithCash:UI', 'UI:ProcessPaymentType:WithCash', new_content)
    new_content = re.sub(r'Enum:ProcessPaymentType:WithCurrency:UI', 'UI:ProcessPaymentType:WithCurrency', new_content)
    new_content = re.sub(r'Enum:ProcessPaymentType:Return:UI', 'UI:ProcessPaymentType:Return', new_content)
    new_content = re.sub(r'Enum:ProcessPaymentType:Consignment:UI', 'UI:ProcessPaymentType:Consignment', new_content)
    new_content = re.sub(r'Enum:ProcessPaymentType:Reservation:UI', 'UI:ProcessPaymentType:Reservation', new_content)
    
    if new_content != content:
        with codecs.open(filepath, 'w', 'utf-8') as f:
            f.write(new_content)
        print(f"Updated {filepath}")

for root, dirs, files in os.walk(project_dir):
    for f in files:
        if f.endswith('.cs') or f.endswith('.razor'):
            replace_in_file(os.path.join(root, f))
