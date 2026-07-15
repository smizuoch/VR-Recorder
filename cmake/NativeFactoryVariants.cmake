include_guard(GLOBAL)

function(vrrecorder_declare_native_factory_options)
    foreach(family IN ITEMS MEDIA ENCODER_PROBE SPOUT STEAMVR)
        set(variable "VRRECORDER_${family}_FACTORY_VARIANT")
        if(NOT DEFINED ${variable})
            set(
                ${variable}
                "UNAVAILABLE"
                CACHE STRING
                "Select exactly one ${family} native factory implementation")
        endif()
        set_property(
            CACHE ${variable}
            PROPERTY STRINGS UNAVAILABLE PRODUCTION)
    endforeach()

    option(
        VRRECORDER_REQUIRE_FULL_PRODUCTION_FACTORIES
        "Reject any native target that still selects an unavailable factory"
        OFF)
endfunction()

function(vrrecorder_resolve_native_factory_source output family variant)
    if(family STREQUAL "MEDIA")
        set(unavailable_source "src/unavailable_media_backend.cpp")
        set(production_source "src/production_media_backend.cpp")
    elseif(family STREQUAL "ENCODER_PROBE")
        set(unavailable_source "src/unavailable_encoder_probe_backend.cpp")
        set(production_source "src/production_encoder_probe_backend.cpp")
    elseif(family STREQUAL "SPOUT")
        set(unavailable_source "src/unavailable_spout_source_backend.cpp")
        set(production_source "src/spout2_source_backend.cpp")
    elseif(family STREQUAL "STEAMVR")
        set(unavailable_source "src/unavailable_steamvr_input_backend.cpp")
        set(production_source "src/openvr_steamvr_input_backend.cpp")
    else()
        message(FATAL_ERROR "Unknown native factory family: '${family}'")
    endif()

    if(variant STREQUAL "UNAVAILABLE")
        set(source "${unavailable_source}")
    elseif(variant STREQUAL "PRODUCTION")
        set(source "${production_source}")
    else()
        message(
            FATAL_ERROR
            "${family} factory variant must be exactly UNAVAILABLE or PRODUCTION; got '${variant}'")
    endif()

    set(${output} "${source}" PARENT_SCOPE)
endfunction()

function(vrrecorder_select_native_factory_sources output source_root)
    if(source_root STREQUAL "" OR NOT IS_ABSOLUTE "${source_root}")
        message(FATAL_ERROR "The native factory source root must be absolute")
    endif()

    set(selected_sources "")
    set(production_count 0)
    foreach(family IN ITEMS MEDIA ENCODER_PROBE SPOUT STEAMVR)
        set(variable "VRRECORDER_${family}_FACTORY_VARIANT")
        if(NOT DEFINED ${variable})
            message(FATAL_ERROR "${variable} must be defined")
        endif()
        set(variant "${${variable}}")
        vrrecorder_resolve_native_factory_source(
            relative_source "${family}" "${variant}")
        set(absolute_source "${source_root}/${relative_source}")
        if(NOT EXISTS "${absolute_source}" OR IS_DIRECTORY "${absolute_source}")
            message(
                FATAL_ERROR
                "Selected ${family}/${variant} factory source is missing: ${absolute_source}")
        endif()
        list(APPEND selected_sources "${absolute_source}")
        if(variant STREQUAL "PRODUCTION")
            math(EXPR production_count "${production_count} + 1")
        endif()
    endforeach()

    if(VRRECORDER_REQUIRE_FULL_PRODUCTION_FACTORIES AND
       NOT production_count EQUAL 4)
        message(
            FATAL_ERROR
            "A full production native target requires MEDIA, ENCODER_PROBE, SPOUT, and STEAMVR factories to all select PRODUCTION")
    endif()

    if((VRRECORDER_MEDIA_FACTORY_VARIANT STREQUAL "PRODUCTION" OR
        VRRECORDER_ENCODER_PROBE_FACTORY_VARIANT STREQUAL "PRODUCTION") AND
       NOT VRRECORDER_ENABLE_FFMPEG_ADAPTERS)
        message(
            FATAL_ERROR
            "Production media or encoder-probe factories require the pinned FFmpeg SDK")
    endif()

    if(VRRECORDER_SPOUT_FACTORY_VARIANT STREQUAL "PRODUCTION" AND
       NOT VRRECORDER_ENABLE_SPOUT2_ADAPTER)
        message(
            FATAL_ERROR
            "The production Spout factory requires the pinned Spout2 SDK")
    endif()

    if(VRRECORDER_STEAMVR_FACTORY_VARIANT STREQUAL "PRODUCTION" AND
       NOT VRRECORDER_ENABLE_OPENVR_ADAPTER)
        message(
            FATAL_ERROR
            "The production SteamVR factory requires the pinned OpenVR SDK")
    endif()

    set(unique_sources ${selected_sources})
    list(REMOVE_DUPLICATES unique_sources)
    list(LENGTH selected_sources selected_count)
    list(LENGTH unique_sources unique_count)
    if(NOT selected_count EQUAL 4 OR NOT unique_count EQUAL 4)
        message(
            FATAL_ERROR
            "Native factory selection must resolve to exactly four distinct sources")
    endif()

    set(${output} "${selected_sources}" PARENT_SCOPE)
endfunction()

function(vrrecorder_write_native_factory_selection_intent output_path)
    if(output_path STREQUAL "" OR NOT IS_ABSOLUTE "${output_path}")
        message(FATAL_ERROR "Factory selection intent path must be absolute")
    endif()

    foreach(family IN ITEMS MEDIA ENCODER_PROBE SPOUT STEAMVR)
        set(variable "VRRECORDER_${family}_FACTORY_VARIANT")
        if(NOT DEFINED ${variable})
            message(FATAL_ERROR "${variable} must be defined")
        endif()
        vrrecorder_resolve_native_factory_source(
            "${family}_SOURCE" "${family}" "${${variable}}")
        cmake_path(GET ${family}_SOURCE FILENAME ${family}_SOURCE_BASENAME)
    endforeach()

    if(VRRECORDER_REQUIRE_FULL_PRODUCTION_FACTORIES)
        set(full_production_required true)
    else()
        set(full_production_required false)
    endif()

    cmake_path(GET output_path PARENT_PATH evidence_directory)
    file(MAKE_DIRECTORY "${evidence_directory}")
    file(
        WRITE "${output_path}"
        "{\n"
        "  \"schemaVersion\": 1,\n"
        "  \"evidenceKind\": \"native-factory-selection-intent\",\n"
        "  \"fullProductionRequired\": ${full_production_required},\n"
        "  \"media\": {\"variant\": \"${VRRECORDER_MEDIA_FACTORY_VARIANT}\", \"source\": \"${MEDIA_SOURCE_BASENAME}\"},\n"
        "  \"encoderProbe\": {\"variant\": \"${VRRECORDER_ENCODER_PROBE_FACTORY_VARIANT}\", \"source\": \"${ENCODER_PROBE_SOURCE_BASENAME}\"},\n"
        "  \"spout\": {\"variant\": \"${VRRECORDER_SPOUT_FACTORY_VARIANT}\", \"source\": \"${SPOUT_SOURCE_BASENAME}\"},\n"
        "  \"steamVr\": {\"variant\": \"${VRRECORDER_STEAMVR_FACTORY_VARIANT}\", \"source\": \"${STEAMVR_SOURCE_BASENAME}\"}\n"
        "}\n")
endfunction()
