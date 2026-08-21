extends TestCase

## Автопроверка механических правил официального GDScript style guide
## по всем скриптам проекта: имена файлов/каталогов, табы, хвостовые пробелы,
## длина строк, пробел после `#`, слитный `:=`, нули во float, нижний hex.
## Немеханические правила (порядок регионов, переносы) — на ревью.

#region CONSTANTS

## Корень обхода.
const ROOT: String = "res://"

## Каталоги, которые не проверяем.
const SKIP_DIRS: Array[String] = [".godot", "addons"]

## Максимальная длина строки по гайду.
const MAX_LINE_LENGTH: int = 100

#endregion


#region VARIABLES
#region REGULAR_PRIVATE

## Кэш содержимого скриптов: путь -> массив строк.
var _files: Dictionary[String, PackedStringArray] = {}

#endregion
#endregion


#region FUNCTIONS
#region TESTS

## Имена файлов и каталогов — snake_case.
func test_snake_case_names() -> void:
	var name_re: RegEx = RegEx.create_from_string("^[a-z0-9_]+(\\.gd)?$")
	for path: String in _collect().keys():
		for part: String in path.trim_prefix(ROOT).split("/"):
			if name_re.search(part) == null:
				fail("%s: name `%s` is not snake_case" % [path, part])
	check_true(true, "all names are snake_case")


## Отступы — только табы, без пробелов.
func test_tabs_indentation() -> void:
	for path: String in _collect().keys():
		var lines: PackedStringArray = _collect()[path]
		for idx: int in lines.size():
			var indent: String = lines[idx].substr(
					0, lines[idx].length() - lines[idx].lstrip(" \t").length()
			)
			if " " in indent:
				fail("%s:%d: spaces in indentation" % [path, idx + 1])
	check_true(true, "indentation is tabs-only")


## Нет хвостовых пробелов после содержимого.
## Строки из одних отступов не проверяем (конвенция пустых строк с табами).
func test_no_trailing_whitespace() -> void:
	for path: String in _collect().keys():
		var lines: PackedStringArray = _collect()[path]
		for idx: int in lines.size():
			var line: String = lines[idx]
			if line.strip_edges() == "":
				continue
			if line != line.strip_edges(false, true):
				fail("%s:%d: trailing whitespace" % [path, idx + 1])
	check_true(true, "no trailing whitespace")


## Пустые строки сохраняют табуляцию окружения
## (конвенция проекта поверх официального гайда):
## отступ = минимум из отступов соседних непустых строк.
func test_blank_line_indentation() -> void:
	for path: String in _collect().keys():
		var lines: PackedStringArray = _collect()[path]
		for idx: int in lines.size():
			if lines[idx].strip_edges() != "":
				continue
			var want: int = mini(
					_neighbor_indent(lines, idx, -1), _neighbor_indent(lines, idx, +1)
			)
			if lines[idx] != "\t".repeat(want):
				fail("%s:%d: blank line must have %d tab(s)" % [path, idx + 1, want])
	check_true(true, "blank lines keep the surrounding indentation")


## Длина строки — не больше 100 символов.
func test_line_length() -> void:
	for path: String in _collect().keys():
		var lines: PackedStringArray = _collect()[path]
		for idx: int in lines.size():
			if lines[idx].length() > MAX_LINE_LENGTH:
				fail("%s:%d: line is %d chars (max %d)" % [
						path, idx + 1, lines[idx].length(), MAX_LINE_LENGTH,
				])
	check_true(true, "line length is within the limit")


## Комментарии начинаются с пробела после `#`;
## исключения — `#region`/`#endregion`.
func test_comment_spacing() -> void:
	for path: String in _collect().keys():
		var lines: PackedStringArray = _collect()[path]
		for idx: int in lines.size():
			var comment: String = _comment_part(lines[idx])
			if comment == "":
				continue
			if comment.begins_with("#region") or comment.begins_with("#endregion"):
				continue
			var text: String = comment.lstrip("#")
			if text != "" and not text.begins_with(" "):
				fail("%s:%d: no space after `#`" % [path, idx + 1])
	check_true(true, "comments start with a space")


## Вывод типа пишется слитно: `:=`, а не `: =`.
func test_inferred_type_spacing() -> void:
	for path: String in _collect().keys():
		var lines: PackedStringArray = _collect()[path]
		for idx: int in lines.size():
			if ": =" in _code_part(lines[idx]):
				fail("%s:%d: `: =` instead of `:=`" % [path, idx + 1])
	check_true(true, "inferred types use `:=`")


## Float-литералы — с ведущим и замыкающим нулем: `0.5`, `13.0`.
func test_float_literals() -> void:
	var leading_re: RegEx = RegEx.create_from_string("(^|[^0-9A-Za-z_.])\\.[0-9]")
	var trailing_re: RegEx = RegEx.create_from_string("[0-9]\\.($|[^0-9A-Za-z_])")
	for path: String in _collect().keys():
		var lines: PackedStringArray = _collect()[path]
		for idx: int in lines.size():
			var code: String = _code_part(lines[idx])
			if leading_re.search(code) != null:
				fail("%s:%d: float without a leading zero" % [path, idx + 1])
			if trailing_re.search(code) != null:
				fail("%s:%d: float without a trailing zero" % [path, idx + 1])
	check_true(true, "float literals have leading/trailing zeros")


## Hex-литералы — в нижнем регистре: `0xfb8c0b`.
func test_hex_literals_lowercase() -> void:
	var hex_re: RegEx = RegEx.create_from_string("0x[0-9a-fA-F]+")
	for path: String in _collect().keys():
		var lines: PackedStringArray = _collect()[path]
		for idx: int in lines.size():
			for found: RegExMatch in hex_re.search_all(_code_part(lines[idx])):
				var literal: String = found.get_string()
				if literal != literal.to_lower():
					fail("%s:%d: uppercase hex `%s`" % [path, idx + 1, literal])
	check_true(true, "hex literals are lowercase")


## Порядок секций скрипта по гайду: signal → enum → const → static var →
## @export → var → @onready → func → вложенные class. Проверяется верхний
## уровень и содержимое вложенных классов.
func test_section_order() -> void:
	for path: String in _collect().keys():
		var lines: PackedStringArray = _collect()[path]
		_check_sections(path, lines, 0, lines.size(), 0)
	check_true(true, "script sections follow the guide order")


## Порядок регионов доступа: `*PUBLIC*` раньше `*PRIVATE*`
## среди соседей одного уровня вложенности.
func test_region_access_order() -> void:
	for path: String in _collect().keys():
		var lines: PackedStringArray = _collect()[path]
		var seen_private: Array[bool] = [false]
		for idx: int in lines.size():
			var comment: String = _comment_part(lines[idx]).strip_edges()
			if comment.begins_with("#region"):
				var region: String = comment.trim_prefix("#region").strip_edges()
				if "PUBLIC" in region and seen_private[-1]:
					fail("%s:%d: PUBLIC region after a PRIVATE one" % [path, idx + 1])
				if "PRIVATE" in region:
					seen_private[-1] = true
				seen_private.append(false)
			elif comment.begins_with("#endregion") and seen_private.size() > 1:
				seen_private.pop_back()
	check_true(true, "public regions precede private ones")


## Регистр идентификаторов: функции/переменные/сигналы — snake_case,
## константы — CONSTANT_CASE (или PascalCase для классов),
## enum и классы — PascalCase.
func test_identifier_case() -> void:
	var rules: Array[Array] = [
		["(?:^|\\s)func\\s+([A-Za-z0-9_]+)", "^_*[a-z][a-z0-9_]*$", "function"],
		["(?:^|\\s)var\\s+([A-Za-z0-9_]+)", "^_*[a-z][a-z0-9_]*$", "variable"],
		["^signal\\s+([A-Za-z0-9_]+)", "^[a-z][a-z0-9_]*$", "signal"],
		["(?:^|\\s)const\\s+([A-Za-z0-9_]+)",
				"^[A-Z][A-Z0-9_]*$|^[A-Z][A-Za-z0-9]*$", "constant"],
		["(?:^|\\s)enum\\s+([A-Za-z0-9_]+)", "^[A-Z][A-Za-z0-9]*$", "enum"],
		["^(?:@\\w+\\s+)*class\\s+([A-Za-z0-9_]+)", "^[A-Z][A-Za-z0-9]*$", "class"],
		["^class_name\\s+([A-Za-z0-9_]+)", "^[A-Z][A-Za-z0-9]*$", "class_name"],
	]
	for path: String in _collect().keys():
		var lines: PackedStringArray = _collect()[path]
		for idx: int in lines.size():
			var code: String = _code_part(lines[idx]).strip_edges()
			for rule: Array in rules:
				var found: RegExMatch = RegEx.create_from_string(rule[0]).search(code)
				if found == null:
					continue
				var identifier: String = found.get_string(1)
				if RegEx.create_from_string(rule[1]).search(identifier) == null:
					fail("%s:%d: %s `%s` breaks the case convention" % [
							path, idx + 1, rule[2], identifier,
					])
	check_true(true, "identifiers follow case conventions")


## Кавычки — двойные; одинарные допустимы только ради экранирования
## двойных внутри строки.
func test_string_quotes() -> void:
	for path: String in _collect().keys():
		var lines: PackedStringArray = _collect()[path]
		for idx: int in lines.size():
			for literal: String in _single_quoted(lines[idx]):
				if "\"" not in literal:
					fail("%s:%d: single quotes without a need: '%s'" % [
							path, idx + 1, literal,
					])
	check_true(true, "double quotes are the default")

#endregion

#region REGULAR_PRIVATE

## Ленивый сбор всех скриптов проекта в кэш [member _files].
func _collect() -> Dictionary[String, PackedStringArray]:
	if not _files.is_empty():
		return _files
	
	var paths: Array[String] = []
	_walk(ROOT, paths)
	for path: String in paths:
		_files[path] = FileAccess.get_file_as_string(path).split("\n")
	return _files


## Рекурсивный обход каталога [param dir_path] со сбором путей скриптов.
func _walk(dir_path: String, out: Array[String]) -> void:
	var dir: DirAccess = DirAccess.open(dir_path)
	if dir == null:
		return
	
	for sub_dir: String in dir.get_directories():
		if sub_dir in SKIP_DIRS or sub_dir.begins_with("."):
			continue
		_walk(dir_path.path_join(sub_dir), out)
	
	for file: String in dir.get_files():
		if file.ends_with(".gd"):
			out.append(dir_path.path_join(file))


## Проверка порядка секций в диапазоне строк [param start]..[param end]
## на уровне вложенности [param indent]; вложенные классы — рекурсивно.
func _check_sections(
		path: String, lines: PackedStringArray, start: int, end: int, indent: int
) -> void:
	var max_category: int = 0
	var idx: int = start
	while idx < end:
		var code: String = _code_part(lines[idx])
		var stripped: String = code.strip_edges()
		var line_indent: int = code.length() - code.lstrip("\t").length()
		if stripped == "" or line_indent != indent:
			idx += 1
			continue
		var category: int = _section_category(stripped)
		if category == -1:
			idx += 1
			continue
		if category < max_category:
			fail("%s:%d: `%s` is out of the section order" % [
					path, idx + 1, stripped.substr(0, 32),
			])
		max_category = maxi(max_category, category)
		if category == 9:
			var block_end: int = _block_end(lines, idx, indent)
			_check_sections(path, lines, idx + 1, block_end, indent + 1)
			idx = block_end
			continue
		idx += 1


## Категория секции для строки кода [param stripped]
## (порядок по гайду); -1 — строка вне классификации.
func _section_category(stripped: String) -> int:
	if RegEx.create_from_string("^(@\\w+(\\(.*\\))?\\s+)*class\\s").search(stripped):
		return 9
	if stripped.begins_with("signal "):
		return 1
	if stripped.begins_with("enum ") or stripped == "enum":
		return 2
	if stripped.begins_with("const "):
		return 3
	if stripped.begins_with("static var "):
		return 4
	if stripped.begins_with("@export"):
		return 5
	if stripped.begins_with("@onready"):
		return 7
	if stripped.begins_with("var "):
		return 6
	if stripped.begins_with("func ") or stripped.begins_with("static func "):
		return 8
	return -1


## Первая строка после [param start], чей код на отступе не глубже
## [param indent], — конец блока; иначе конец файла.
func _block_end(lines: PackedStringArray, start: int, indent: int) -> int:
	for idx: int in range(start + 1, lines.size()):
		var code: String = _code_part(lines[idx])
		if code.strip_edges() == "":
			continue
		if code.length() - code.lstrip("\t").length() <= indent:
			return idx
	return lines.size()


## Содержимое одинарных строковых литералов строки [param line].
func _single_quoted(line: String) -> Array[String]:
	var literals: Array[String] = []
	var in_string: bool = false
	var quote: String = ""
	var current: String = ""
	var idx: int = 0
	while idx < line.length():
		var symbol: String = line[idx]
		if in_string:
			if symbol == "\\":
				current += line.substr(idx, 2)
				idx += 2
				continue
			if symbol == quote:
				in_string = false
				if quote == "'":
					literals.append(current)
			else:
				current += symbol
			idx += 1
			continue
		if symbol == "\"" or symbol == "'":
			in_string = true
			quote = symbol
			current = ""
			idx += 1
			continue
		if symbol == "#":
			break
		idx += 1
	return literals


## Отступ (в табах) ближайшей непустой строки от [param idx]
## в направлении [param step]; 0, если такой строки нет.
func _neighbor_indent(lines: PackedStringArray, idx: int, step: int) -> int:
	var cursor: int = idx + step
	while cursor >= 0 and cursor < lines.size():
		if lines[cursor].strip_edges() != "":
			return lines[cursor].length() - lines[cursor].lstrip("\t").length()
		cursor += step
	return 0


## Код строки [param line] без строковых литералов и комментария.
func _code_part(line: String) -> String:
	var result: String = ""
	var in_string: bool = false
	var quote: String = ""
	var idx: int = 0
	while idx < line.length():
		var symbol: String = line[idx]
		if in_string:
			if symbol == "\\":
				idx += 2
				continue
			if symbol == quote:
				in_string = false
			idx += 1
			continue
		if symbol == "\"" or symbol == "'":
			in_string = true
			quote = symbol
			idx += 1
			continue
		if symbol == "#":
			break
		result += symbol
		idx += 1
	return result


## Комментарий строки [param line] (от `#` до конца) или пустая строка.
func _comment_part(line: String) -> String:
	var in_string: bool = false
	var quote: String = ""
	var idx: int = 0
	while idx < line.length():
		var symbol: String = line[idx]
		if in_string:
			if symbol == "\\":
				idx += 2
				continue
			if symbol == quote:
				in_string = false
			idx += 1
			continue
		if symbol == "\"" or symbol == "'":
			in_string = true
			quote = symbol
			idx += 1
			continue
		if symbol == "#":
			return line.substr(idx)
		idx += 1
	return ""

#endregion
#endregion
