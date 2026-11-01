<!--
Copyright 2026 Penguin

Licensed under the Apache License, Version 2.0 (the "License");
you may not use this file except in compliance with the License.
You may obtain a copy of the License at

    http://www.apache.org/licenses/LICENSE-2.0

Unless required by applicable law or agreed to in writing, software
distributed under the License is distributed on an "AS IS" BASIS,
WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
See the License for the specific language governing permissions and
limitations under the License.

Defines the license-header rules continued from the package AGENTS.md.
-->

# Licensing

## License headers

Add the Apache License 2.0 notice to the beginning of every repository-owned text file that supports comments.

Use the comment syntax appropriate for the file type while preserving the following text:

```text
Copyright [yyyy] [name of copyright owner]

Licensed under the Apache License, Version 2.0 (the "License");
you may not use this file except in compliance with the License.
You may obtain a copy of the License at

    http://www.apache.org/licenses/LICENSE-2.0

Unless required by applicable law or agreed to in writing, software
distributed under the License is distributed on an "AS IS" BASIS,
WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
See the License for the specific language governing permissions and
limitations under the License.
```

### Year policy

* Use the year in which the file was first created.
* Do not update the copyright year merely because the file was modified.
* Preserve the existing year or year range when editing a file that already has a license header.
* For files first created in 2026, use `Copyright 2026 [name of copyright owner]`.

### Placement

* The license notice must appear before imports, includes, declarations, documentation headings, or executable code.
* A required interpreter directive such as a Unix shebang may remain on the first line; place the license notice immediately after it.
* Preserve mandatory format-specific declarations when placing a comment before them would make the file invalid.

### Comment syntax

Use the native comment syntax of the file.

For C#, HLSL, ShaderLab, JavaScript, and similar files:

```text
/*
 * Copyright [yyyy] [name of copyright owner]
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *     http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */
```

For Markdown files, use an HTML comment so the notice is not rendered:

```html
<!--
Copyright [yyyy] [name of copyright owner]

Licensed under the Apache License, Version 2.0 (the "License");
you may not use this file except in compliance with the License.
You may obtain a copy of the License at

    http://www.apache.org/licenses/LICENSE-2.0

Unless required by applicable law or agreed to in writing, software
distributed under the License is distributed on an "AS IS" BASIS,
WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
See the License for the specific language governing permissions and
limitations under the License.
-->
```

For shell scripts, YAML, TOML, Python, and other line-comment formats, prefix each line with the appropriate comment marker.

### Exclusions

Do not insert a license header into:

* `LICENSE` itself
* Binary files, images, archives, or other non-text assets
* Files whose format does not support comments, including strict JSON
* Unity-generated `.meta` files
* Generated code or generated artifacts
* Lock files
* Third-party or vendored files
* Files copied from another project that retain a different valid license notice

For files that cannot contain comments, keep the repository-level `LICENSE` file and applicable package metadata such as the `license` field in `package.json`.

Do not alter a third-party copyright or license notice without explicit approval.
